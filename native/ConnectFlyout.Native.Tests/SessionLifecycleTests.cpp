#include "FakeHeadset.h"

#include "sony/protocol/HeadsetController.h"
#include "sony/protocol/SonyProtocolSession.h"
#include "sony/transport/ITransport.h"

#include <gtest/gtest.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <thread>

using namespace std::chrono_literals;

using sony::SonyErrorCode;
using sony::SonyException;
using sony::protocol::HeadsetController;
using sony::protocol::SonyProtocolSession;
using sony::test::FakeHeadset;
using sony::test::kTestAddress;
using sony::test::scriptXm6Connect;

namespace {

// Transport whose receive blocks like a real socket: it waits out the full
// 2.5 s receive timeout unless disconnect() closes it first.
class BlockingTransport final : public sony::transport::ITransport {
public:
    void connect(const sony::transport::DeviceAddress&) override {
        std::lock_guard lock(_mutex);
        _connected = true;
        _closed = false;
    }

    void disconnect() noexcept override {
        {
            std::lock_guard lock(_mutex);
            _connected = false;
            _closed = true;
        }
        _changed.notify_all();
    }

    [[nodiscard]] bool isConnected() const noexcept override {
        std::lock_guard lock(_mutex);
        return _connected;
    }

    size_t send(std::span<const std::byte> data) override {
        std::lock_guard lock(_mutex);
        if (!_connected) {
            throw SonyException(SonyErrorCode::Disconnected, "closed");
        }
        return data.size();
    }

    size_t receive(std::span<std::byte>) override {
        std::unique_lock lock(_mutex);
        _changed.wait_for(lock, 2500ms, [this] { return _closed; });
        if (_closed) {
            throw SonyException(SonyErrorCode::Disconnected, "closed");
        }
        throw SonyException(SonyErrorCode::Timeout, "receive timed out");
    }

private:
    mutable std::mutex _mutex;
    std::condition_variable _changed;
    bool _connected{false};
    bool _closed{false};
};

// Scripted headset whose link only opens once the test releases it, so a
// disconnect can land in the middle of a connect.
class GatedHeadset final : public FakeHeadset {
public:
    void connect(const sony::transport::DeviceAddress& address) override {
        {
            std::unique_lock lock(_gateMutex);
            _gateChanged.wait(lock, [this] { return _open; });
        }
        FakeHeadset::connect(address);
    }

    void open() {
        {
            std::lock_guard lock(_gateMutex);
            _open = true;
        }
        _gateChanged.notify_all();
    }

private:
    std::mutex _gateMutex;
    std::condition_variable _gateChanged;
    bool _open{false};
};

} // namespace

TEST(SessionLifecycle, DisconnectDoesNotWaitForTheReceiveTimeout) {
    SonyProtocolSession session(std::make_unique<BlockingTransport>());
    session.connect(kTestAddress);
    std::this_thread::sleep_for(50ms);

    const auto started = std::chrono::steady_clock::now();
    session.disconnect();

    EXPECT_LT(std::chrono::steady_clock::now() - started, 500ms);
    EXPECT_FALSE(session.isConnected());
}

TEST(SessionLifecycle, DisconnectDuringConnectCancelsTheConnect) {
    auto transport = std::make_unique<GatedHeadset>();
    auto* headset = transport.get();
    HeadsetController controller(std::move(transport), "WF-1000XM6");
    scriptXm6Connect(*headset);

    std::atomic<bool> threw{false};
    std::thread connecting([&] {
        try {
            controller.connect(kTestAddress);
        } catch (const SonyException& ex) {
            threw = ex.code() == SonyErrorCode::Disconnected;
        }
    });
    std::this_thread::sleep_for(50ms);

    controller.disconnect();
    headset->open();
    connecting.join();

    EXPECT_TRUE(threw.load());
    EXPECT_FALSE(controller.isConnected());
}

// A device that sends a frame start and then never ends the frame must not grow the receive
// buffer without limit; once it finally sends a proper frame, that frame still gets through.
TEST(SessionLifecycle, EndlessFrameWithoutEndMarkerStaysBounded) {
    FakeHeadset transport;
    SonyProtocolSession session(&transport);
    std::atomic<int> notifications{0};
    session.onNotification([&](const sony::protocol::SonyFrame&) { ++notifications; });
    session.connect(kTestAddress);

    std::vector<uint8_t> garbage(64 * 1024, 0x00);
    garbage.front() = sony::protocol::FrameCodec::START_MARKER;
    transport.queueIncoming(garbage);
    ASSERT_TRUE(sony::test::waitUntil([&] { return transport.incomingBytesAvailable() == 0; }));
    std::this_thread::sleep_for(50ms);

    EXPECT_LE(session.bufferedByteCount(), sony::protocol::FrameCodec::MAX_FRAME_SIZE * 2);

    transport.notify({0x25, 0x00, 0x50, 0x00});
    EXPECT_TRUE(sony::test::waitUntil([&] { return notifications.load() == 1; }));
    session.disconnect();
}
