#include "FakeHeadset.h"

#include "sony/protocol/HeadsetController.h"

#include <gtest/gtest.h>

#include <atomic>
#include <memory>
#include <mutex>
#include <thread>

using namespace std::chrono_literals;

using sony::SonyErrorCode;
using sony::SonyException;
using sony::protocol::DataType;
using sony::protocol::DeviceState;
using sony::protocol::FrameCodec;
using sony::protocol::HeadsetController;
using sony::protocol::NoiseControlMode;
using sony::protocol::NoiseControlState;
using sony::protocol::SonyFrame;
using sony::test::FakeHeadset;
using sony::test::kTestAddress;
using sony::test::Payload;
using sony::test::scriptXm6Connect;
using sony::test::waitUntil;

namespace {

class Xm6Connection : public ::testing::Test {
protected:
    void SetUp() override {
        auto transport = std::make_unique<FakeHeadset>();
        headset = transport.get();
        controller = std::make_unique<HeadsetController>(std::move(transport), "WF-1000XM6");
    }

    void connect() {
        scriptXm6Connect(*headset);
        controller->connect(kTestAddress);
    }

    SonyErrorCode errorCodeOf(const std::function<void()>& action) {
        try {
            action();
        } catch (const SonyException& ex) {
            return ex.code();
        }
        ADD_FAILURE() << "Expected a SonyException";
        return SonyErrorCode::ProtocolViolation;
    }

    FakeHeadset* headset{};
    std::unique_ptr<HeadsetController> controller;
};

} // namespace

TEST_F(Xm6Connection, ConnectReadsFullInitialState) {
    connect();

    ASSERT_TRUE(controller->isConnected());
    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.left.value_or(-1), 85);
    EXPECT_EQ(state.battery.right.value_or(-1), 82);
    EXPECT_EQ(state.battery.caseBattery.value_or(-1), 95);
    EXPECT_TRUE(state.noiseControl.mode == NoiseControlMode::Ambient);
    EXPECT_EQ(state.noiseControl.ambientLevel, 8);
    EXPECT_TRUE(state.noiseControl.focusOnVoice);
    EXPECT_EQ(state.equalizer.preset, 0x10);
    EXPECT_EQ(state.equalizer.clearBass, 0);
    // Ten-band equalizers keep the preset; the five-band curve stays empty.
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{0, 0, 0, 0, 0}));
    EXPECT_TRUE(state.dsee);
    EXPECT_TRUE(state.speakToChat);
    EXPECT_FALSE(state.adaptiveVolume);
    EXPECT_EQ(state.autoPowerOff, 5);
    EXPECT_EQ(state.firmware, "1.6.0");
    EXPECT_EQ(state.codec, "LDAC");
}

TEST_F(Xm6Connection, ConnectStartsWithV2Handshake) {
    connect();

    const auto requests = headset->requests();
    ASSERT_FALSE(requests.empty());
    EXPECT_EQ(requests.front(), (Payload{0x00, 0x00}));
}

TEST_F(Xm6Connection, ConnectSucceedsWhenHandshakeIsUnanswered) {
    scriptXm6Connect(*headset, false);
    controller->connect(kTestAddress);

    EXPECT_TRUE(controller->isConnected());
    EXPECT_EQ(controller->state().codec, "LDAC");
}

TEST_F(Xm6Connection, ConnectFailsCleanlyWhenHeadsetNeverAnswers) {
    EXPECT_EQ(errorCodeOf([&] { controller->connect(kTestAddress); }), SonyErrorCode::Timeout);
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, ConnectFailsWhenTheLinkDropsDuringTheInitialReads) {
    // Optional reads shrug off errors, so a headset that drops the link partway through them
    // used to leave connect() reporting success on a dead link
    headset->reply({{0x01, 0x00, 0x03, 0x00, 0x30, 0x02, 0x00, 0x00}});           // 00 00 handshake
    headset->reply({{0x23, 0x09, 85, 0x00, 82, 0x00, 0x64, 0x64}});               // 22 09 battery
    headset->reply({{0x23, 0x0a, 95, 0x00, 0x1e}});                               // 22 0a case
    headset->reply({{0x67, 0x19, 0x01, 0x01, 0x01, 0x01, 0x08, 0x00, 0x00}});     // 66 19 noise control
    headset->reply({{0x57, 0x00, 0x10, 0x0a, 0x06, 0x06, 0x07, 0x08, 0x06, 0x05, 0x06, 0x06, 0x06, 0x06}}); // 56 00 equalizer
    headset->reply({{0xe7, 0x01, 0x01}});                                         // e6 01 DSEE
    std::thread dropper([&] {
        waitUntil([&] { return headset->requests().size() >= 7; });
        headset->simulateDisconnect();
    });

    EXPECT_EQ(errorCodeOf([&] { controller->connect(kTestAddress); }), SonyErrorCode::Disconnected);
    dropper.join();
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, ConnectFailsWhenLinkIsRefused) {
    headset->setFailConnect(true);

    EXPECT_EQ(errorCodeOf([&] { controller->connect(kTestAddress); }), SonyErrorCode::TransportFailure);
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, RequestSequenceNumbersAlternate) {
    connect();

    const auto sequences = headset->requestSequences();
    ASSERT_GT(sequences.size(), 2u);
    for (size_t i = 1; i < sequences.size(); ++i) {
        EXPECT_NE(sequences[i], sequences[i - 1]) << "request " << i;
    }
}

TEST_F(Xm6Connection, SetNoiseControlSendsV2BytesAndPublishesState) {
    connect();
    std::vector<DeviceState> published;
    controller->onStateChanged([&](const DeviceState& state) { published.push_back(state); });

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::NoiseCancelling, .ambientLevel = 0, .focusOnVoice = false});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x19, 0x01, 0x01, 0x00, 0x00, 0x08, 0x00, 0x00}));
    EXPECT_TRUE(controller->state().noiseControl.mode == NoiseControlMode::NoiseCancelling);
    ASSERT_EQ(published.size(), 1u);
    EXPECT_TRUE(published.front().noiseControl.mode == NoiseControlMode::NoiseCancelling);
}

TEST_F(Xm6Connection, SetAmbientSendsLevelAndVoice) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::Ambient, .ambientLevel = 14, .focusOnVoice = true});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x19, 0x01, 0x01, 0x01, 0x01, 0x0e, 0x00, 0x00}));
    EXPECT_EQ(controller->state().noiseControl.ambientLevel, 14);
}

TEST_F(Xm6Connection, SettersSendV2Bytes) {
    connect();

    headset->reply();
    controller->setEqualizerPreset(0x16);
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x00, 0x16, 0x00}));

    headset->reply();
    controller->setEqualizerCustom(3, {-10, -5, 0, 5, 10});
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x00, 0xa0, 0x06, 0x0d, 0x00, 0x05, 0x0a, 0x0f, 0x14}));

    headset->reply();
    controller->setDsee(false);
    EXPECT_EQ(headset->requests().back(), (Payload{0xe8, 0x01, 0x00}));

    headset->reply();
    controller->setSpeakToChat(true);
    EXPECT_EQ(headset->requests().back(), (Payload{0xf8, 0x0c, 0x00, 0x01}));

    headset->reply();
    controller->setAdaptiveVolume(false);
    EXPECT_EQ(headset->requests().back(), (Payload{0xf8, 0x0a, 0x01}));

    headset->reply();
    controller->setAutoPowerOff(2);
    EXPECT_EQ(headset->requests().back(), (Payload{0x28, 0x05, 0x01, 0x01}));

    headset->reply();
    controller->powerOff();
    EXPECT_EQ(headset->requests().back(), (Payload{0x24, 0x03, 0x01}));

    const DeviceState state = controller->state();
    EXPECT_EQ(state.equalizer.preset, 0xa0);
    EXPECT_EQ(state.equalizer.clearBass, 3);
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{-10, -5, 0, 5, 10}));
    EXPECT_FALSE(state.dsee);
    EXPECT_TRUE(state.speakToChat);
    EXPECT_FALSE(state.adaptiveVolume);
    EXPECT_EQ(state.autoPowerOff, 2);
}

TEST_F(Xm6Connection, CommandRetriesOnceAfterTimeout) {
    connect();

    headset->ignore();
    headset->reply();
    controller->setDsee(false);

    const auto requests = headset->requests();
    EXPECT_EQ(requests[requests.size() - 1], (Payload{0xe8, 0x01, 0x00}));
    EXPECT_EQ(requests[requests.size() - 2], (Payload{0xe8, 0x01, 0x00}));
    EXPECT_FALSE(controller->state().dsee);
}

TEST_F(Xm6Connection, CommandFailsAfterSecondTimeoutAndKeepsState) {
    connect();

    headset->ignore();
    headset->ignore();

    EXPECT_EQ(errorCodeOf([&] { controller->setDsee(false); }), SonyErrorCode::Timeout);
    EXPECT_TRUE(controller->state().dsee);
}

TEST_F(Xm6Connection, CommandThatNeverAnswersDropsTheLinkSoItReconnects) {
    // A real XM6 once stopped ACKing every command on one link while a fresh link worked,
    // so an unanswered command reopens the link instead of leaving the controls dead.
    connect();
    std::atomic<bool> dropped{false};
    controller->onDisconnected([&] { dropped = true; });

    headset->ignore();
    headset->ignore();
    EXPECT_EQ(errorCodeOf([&] { controller->setDsee(false); }), SonyErrorCode::Timeout);

    EXPECT_TRUE(dropped.load());
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, NotificationUpdatesStateAndPublishes) {
    connect();
    std::mutex mutex;
    std::vector<DeviceState> published;
    controller->onStateChanged([&](const DeviceState& state) {
        std::lock_guard lock(mutex);
        published.push_back(state);
    });

    headset->notify({0x69, 0x19, 0x01, 0x01, 0x00, 0x00, 0x08, 0x00, 0x00});

    EXPECT_TRUE(waitUntil([&] { return controller->state().noiseControl.mode == NoiseControlMode::NoiseCancelling; }));
    std::lock_guard lock(mutex);
    EXPECT_EQ(published.size(), 1u);
}

TEST_F(Xm6Connection, BatteryNotificationUpdatesEarbuds) {
    connect();

    headset->notify({0x25, 0x09, 40, 0x00, 41, 0x01});

    EXPECT_TRUE(waitUntil([&] { return controller->state().battery.left.value_or(-1) == 40; }));
    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.right.value_or(-1), 41);
    EXPECT_TRUE(state.battery.charging);
}

TEST_F(Xm6Connection, DuplicateNotificationIsHandledOnce) {
    connect();
    std::atomic<int> count{0};
    controller->onStateChanged([&](const DeviceState&) { ++count; });

    headset->notifyTwice({0x25, 0x09, 50, 0x00, 49, 0x00});

    EXPECT_TRUE(waitUntil([&] { return count.load() >= 1; }));
    std::this_thread::sleep_for(200ms);
    EXPECT_EQ(count.load(), 1);
}

TEST_F(Xm6Connection, ReplyIsMatchedWhenNotificationArrivesFirst) {
    connect();

    headset->reply({{0x69, 0x19, 0x01, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00}, {0x23, 0x09, 70, 0x00, 71, 0x00, 0x64, 0x64}}); // noise control off, then 22 09 reply
    headset->reply({{0x23, 0x0a, 90, 0x00}});                                               // 22 0a
    controller->refreshBattery();

    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.left.value_or(-1), 70);
    EXPECT_EQ(state.battery.right.value_or(-1), 71);
    EXPECT_EQ(state.battery.caseBattery.value_or(-1), 90);
    EXPECT_TRUE(waitUntil([&] { return controller->state().noiseControl.mode == NoiseControlMode::Off; }));
}

TEST_F(Xm6Connection, CorruptFrameIsDroppedWithoutAck) {
    connect();
    const size_t sentBefore = headset->sentCount();

    std::vector<uint8_t> corrupt = FrameCodec::encode(SonyFrame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x25, 0x09, 10, 0x00, 10, 0x00}});
    ASSERT_EQ(corrupt[corrupt.size() - 2], 0x54);
    corrupt[corrupt.size() - 2] = 0x55;
    headset->queueIncoming(corrupt);
    headset->notify({0x25, 0x09, 33, 0x00, 34, 0x00});

    EXPECT_TRUE(waitUntil([&] { return controller->state().battery.left.value_or(-1) == 33; }));
    EXPECT_EQ(headset->sentCount(), sentBefore + 1);
}

TEST_F(Xm6Connection, LinkDropFailsPendingCommandQuicklyAndReportsDisconnect) {
    connect();
    std::atomic<bool> dropped{false};
    controller->onDisconnected([&] { dropped = true; });

    headset->ignore();
    const auto started = std::chrono::steady_clock::now();
    std::thread dropper([&] {
        std::this_thread::sleep_for(100ms);
        headset->simulateDisconnect();
    });

    EXPECT_EQ(errorCodeOf([&] { controller->setDsee(false); }), SonyErrorCode::Disconnected);
    dropper.join();

    EXPECT_LT(std::chrono::steady_clock::now() - started, 800ms);
    EXPECT_TRUE(waitUntil([&] { return dropped.load(); }));
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, DisconnectDoesNotReportALinkDrop) {
    connect();
    std::atomic<bool> dropped{false};
    controller->onDisconnected([&] { dropped = true; });

    controller->disconnect();
    std::this_thread::sleep_for(100ms);

    EXPECT_FALSE(dropped.load());
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, ReconnectsAfterLinkDrop) {
    connect();
    headset->simulateDisconnect();
    ASSERT_TRUE(waitUntil([&] { return !controller->isConnected(); }));

    scriptXm6Connect(*headset);
    controller->connect(kTestAddress);

    EXPECT_TRUE(controller->isConnected());
    EXPECT_EQ(controller->state().battery.left.value_or(-1), 85);
}

// =========================================================================
// PLAYBACK (MULTIPOINT AUDIO SWITCHING)
// =========================================================================

TEST_F(Xm6Connection, ConnectReadsConnectedPlaybackDevices) {
    connect();

    const auto devices = controller->state().playbackDevices;
    ASSERT_EQ(devices.size(), 2u); // the tablet is paired but not connected
    EXPECT_EQ(devices[0].address, "AA:BB:CC:DD:EE:01");
    EXPECT_EQ(devices[0].name, "DESKTOP");
    EXPECT_TRUE(devices[0].playing);
    EXPECT_EQ(devices[1].name, "Pixel 9");
    EXPECT_FALSE(devices[1].playing);
}

TEST_F(Xm6Connection, PlaybackQueriesGoOverTable2) {
    connect();

    const auto requests = headset->requests();
    const auto types = headset->requestTypes();
    ASSERT_GE(requests.size(), 2u);
    EXPECT_EQ(requests[requests.size() - 2], (Payload{0x06, 0x00}));
    EXPECT_EQ(requests.back(), (Payload{0x36, 0x02}));
    EXPECT_EQ(types.back(), DataType::DataMdrNo2);
}

TEST_F(Xm6Connection, SwitchPlaybackSendsTargetAndUpdatesState) {
    connect();

    Payload confirmed{0x3d, 0x01, 0x00};
    const std::string phone = "AA:BB:CC:DD:EE:02";
    confirmed.insert(confirmed.end(), phone.begin(), phone.end());
    headset->replyTable2({confirmed});
    controller->switchPlayback(phone);

    Payload expected{0x3c, 0x01};
    expected.insert(expected.end(), phone.begin(), phone.end());
    EXPECT_EQ(headset->requests().back(), expected);
    EXPECT_EQ(headset->requestTypes().back(), DataType::DataMdrNo2);
    const auto devices = controller->state().playbackDevices;
    EXPECT_FALSE(devices[0].playing);
    EXPECT_TRUE(devices[1].playing);
}

TEST_F(Xm6Connection, RefusedSwitchThrowsAndKeepsState) {
    connect();

    Payload refused{0x3d, 0x01, 0x02}; // on a call
    const std::string phone = "AA:BB:CC:DD:EE:02";
    refused.insert(refused.end(), phone.begin(), phone.end());
    headset->replyTable2({refused});

    EXPECT_EQ(errorCodeOf([&] { controller->switchPlayback(phone); }), SonyErrorCode::InvalidResponse);
    EXPECT_TRUE(controller->state().playbackDevices[0].playing);
}

TEST_F(Xm6Connection, PlaybackListNotificationUpdatesState) {
    connect();
    std::atomic<int> published{0};
    controller->onStateChanged([&](const DeviceState&) { ++published; });

    headset->notifyTable2(sony::test::xm6PlaybackDevices(2, 0x39));

    ASSERT_TRUE(waitUntil([&] { return published.load() > 0; }));
    const auto devices = controller->state().playbackDevices;
    ASSERT_EQ(devices.size(), 2u);
    EXPECT_TRUE(devices[1].playing);
}

TEST_F(Xm6Connection, ConnectSkipsPlaybackWhenSwitchingIsUnsupported) {
    // Every reply up to the support list, which leaves out source switching (0x31)
    auto transport = std::make_unique<FakeHeadset>();
    auto* fake = transport.get();
    controller = std::make_unique<HeadsetController>(std::move(transport), "WF-1000XM6");
    sony::test::scriptXm6ConnectWithoutSwitching(*fake);

    controller->connect(kTestAddress);

    EXPECT_TRUE(controller->state().playbackDevices.empty());
    EXPECT_NE(fake->requests().back(), (Payload{0x36, 0x02}));
}

// A headset left on "Custom 4" (0xa3) still folds to Manual (0xa0) on connect.
TEST_F(Xm6Connection, ConnectFoldsOtherCustomSlotsIntoManual) {
    scriptXm6Connect(*headset, /*answerHandshake=*/true, /*switching=*/true, /*eqPreset=*/0xa3);
    controller->connect(kTestAddress);

    EXPECT_EQ(controller->state().equalizer.preset, 0xa0);
}
