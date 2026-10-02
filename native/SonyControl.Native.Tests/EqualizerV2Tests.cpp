#include "FakeHeadset.h"

#include "sony/protocol/ProtocolV2.h"
#include "sony/protocol/SonyProtocolSession.h"
#include "sony/protocol/V2Layouts.h"

#include <gtest/gtest.h>

#include <memory>
#include <string>

using sony::protocol::kEqUltModeDefault;
using sony::protocol::ProtocolV2;
using sony::protocol::SonyProtocolSession;
using sony::test::FakeHeadset;
using sony::test::kTestAddress;
using sony::test::Payload;

namespace {

// ProtocolV2 on a scripted headset, with the ULT equalizer layout on or off.
class EqualizerV2 : public ::testing::TestWithParam<bool> {
protected:
    void SetUp() override {
        auto transport = std::make_unique<FakeHeadset>();
        headset = transport.get();
        session = std::make_unique<SonyProtocolSession>(std::move(transport));
        session->connect(kTestAddress);
        protocol = std::make_unique<ProtocolV2>(*session, false, GetParam());
    }

    void TearDown() override { session->disconnect(); }

    Payload lastRequest() const { return headset->requests().back(); }

    FakeHeadset* headset{};
    std::unique_ptr<SonyProtocolSession> session;
    std::unique_ptr<ProtocolV2> protocol;
};

} // namespace

TEST_P(EqualizerV2, GetSendsTheInquiredTypeAndParsesTheReply) {
    if (GetParam()) {
        headset->reply({{0x57, 0x03, 0xa0, 0x01, 0x06, 0x0f, 0x06, 0x0a, 0x0f, 0x12, 0x14}});
    } else {
        headset->reply({{0x57, 0x00, 0xa0, 0x06, 0x0f, 0x06, 0x0a, 0x0f, 0x12, 0x14}});
    }

    const auto state = protocol->getEqualizer();

    EXPECT_EQ(lastRequest(), (Payload{0x56, static_cast<uint8_t>(GetParam() ? 0x03 : 0x00)}));
    EXPECT_EQ(state.preset, 0xa0);
    EXPECT_EQ(state.clearBass, 5);
    EXPECT_EQ(state.bands, (std::array<int, 5>{-4, 0, 5, 8, 10}));
    EXPECT_EQ(state.ultMode, 0x01);
}

TEST_P(EqualizerV2, SetPresetCarriesTheUltByteOnlyOnUltDevices) {
    headset->reply();
    protocol->setEqualizerPreset(0x14, kEqUltModeDefault);

    if (GetParam()) {
        EXPECT_EQ(lastRequest(), (Payload{0x58, 0x03, 0x14, 0x01, 0x00}));   // capture: 58 03 14 01 00
    } else {
        EXPECT_EQ(lastRequest(), (Payload{0x58, 0x00, 0x14, 0x00}));
    }
}

TEST_P(EqualizerV2, SetCustomCarriesTheUltByteOnlyOnUltDevices) {
    headset->reply();
    protocol->setEqualizerCustom(5, {-4, 0, 5, 8, 10}, kEqUltModeDefault);

    if (GetParam()) {
        EXPECT_EQ(lastRequest(), (Payload{0x58, 0x03, 0xa0, 0x01, 0x06, 0x0f, 0x06, 0x0a, 0x0f, 0x12, 0x14}));
    } else {
        EXPECT_EQ(lastRequest(), (Payload{0x58, 0x00, 0xa0, 0x06, 0x0f, 0x06, 0x0a, 0x0f, 0x12, 0x14}));
    }
}

TEST(EqualizerV2Ult, WritesSendTheUltModePassedIn) {
    auto transport = std::make_unique<FakeHeadset>();
    auto* headset = transport.get();
    SonyProtocolSession session(std::move(transport));
    session.connect(kTestAddress);
    ProtocolV2 protocol(session, false, true);

    headset->reply({{0x57, 0x03, 0xa0, 0x02, 0x06, 0x0f, 0x06, 0x0a, 0x0f, 0x12, 0x14}});
    const auto reported = protocol.getEqualizer();
    EXPECT_EQ(reported.ultMode, 0x02);

    headset->reply();
    protocol.setEqualizerPreset(0x14, reported.ultMode);
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x03, 0x14, 0x02, 0x00}));

    headset->reply();
    protocol.setEqualizerCustom(5, {-4, 0, 5, 8, 10}, reported.ultMode);
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x03, 0xa0, 0x02, 0x06, 0x0f, 0x06, 0x0a, 0x0f, 0x12, 0x14}));

    // A different mode (say 00 after the headset reported it) goes out as given
    headset->reply();
    protocol.setEqualizerPreset(0x10, 0x00);
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x03, 0x10, 0x00, 0x00}));
    session.disconnect();
}

// A legacy notification leaves the ULT mode alone; a type 0x03 one updates it.
TEST(EqualizerV2Ult, ParseKeepsTheModeFromTheLastUltFrame) {
    sony::protocol::EqualizerState state;
    ASSERT_TRUE(sony::protocol::parseEqualizer(
        Payload{0x59, 0x03, 0x10, 0x02, 0x06, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a}, state));
    EXPECT_EQ(state.ultMode, 0x02);
    ASSERT_TRUE(sony::protocol::parseEqualizer(Payload{0x59, 0x00, 0x11, 0x06, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a}, state));
    EXPECT_EQ(state.preset, 0x11);
    EXPECT_EQ(state.ultMode, 0x02);
}

// googletest 1.8.1 predates INSTANTIATE_TEST_SUITE_P.
INSTANTIATE_TEST_CASE_P(Layouts, EqualizerV2, ::testing::Bool(),
                        [](const ::testing::TestParamInfo<bool>& info) {
                            return std::string(info.param ? "UltWear" : "Legacy");
                        });
