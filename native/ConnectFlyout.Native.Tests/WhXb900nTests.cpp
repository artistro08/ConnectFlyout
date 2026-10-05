// WH-XB900N (firmware 4.5.2) protocol tests for hardware-verified behavior.

#include "FakeHeadset.h"

#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/HeadsetController.h"
#include "sony/protocol/V1Notifications.h"

#include <gtest/gtest.h>

#include <memory>

using sony::SonyException;
using sony::protocol::applyV1Notification;
using sony::protocol::DataType;
using sony::protocol::DeviceProfileRegistry;
using sony::protocol::DeviceState;
using sony::protocol::HeadsetController;
using sony::protocol::NoiseControlMode;
using sony::protocol::NoiseControlState;
using sony::protocol::ProtocolGeneration;
using sony::protocol::SonyModel;
using sony::protocol::SonyProtocolVersion;
using sony::test::FakeHeadset;
using sony::test::Payload;
using sony::test::kTestAddress;
using sony::test::waitUntil;

namespace {

void scriptWhXb900nConnect(FakeHeadset& headset) {
    headset.reply({{0x01, 0x00, 0x40, 0x10}});
    headset.reply({{0x67, 0x02, 0x10, 0x02, 0x01, 0x01, 0x00, 0x00}});
    headset.reply({{0x11, 0x00, 50, 0x00}});
    headset.reply({{0x67, 0x02, 0x10, 0x02, 0x00, 0x01, 0x01, 0x0a}});
    headset.reply({{0x57, 0x01, 0xa0, 0x06, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a}});
    headset.reply({{0xe7, 0x01, 0x00, 0x00}});
    headset.replyTable2({{0x47, 0x01, 0x01, 0x01}});
    headset.reply({{0x47, 0x01, 0x00}});
    headset.reply({{0x47, 0x02, 0x01}});
    headset.reply({{0xe7, 0x02, 0x00, 0x01}});
    headset.reply({{0xf7, 0x04, 0x01, 0x00, 0x00}});
    headset.reply({{0x05, 0x02, 0x05, '4', '.', '5', '.', '2'}});
    headset.reply({{0x19, 0x00, 0x01}});
}

class WhXb900nConnection : public ::testing::Test {
protected:
    void SetUp() override {
        auto transport = std::make_unique<FakeHeadset>();
        headset = transport.get();
        controller = std::make_unique<HeadsetController>(std::move(transport), "WH-XB900N");
    }

    void connect() {
        scriptWhXb900nConnect(*headset);
        controller->connect(kTestAddress);
    }

    FakeHeadset* headset{};
    std::unique_ptr<HeadsetController> controller;
};

} // namespace

TEST(WhXb900nProfile, UsesVerifiedV1Capabilities) {
    const auto profile = DeviceProfileRegistry::getProfileForDevice("WH-XB900N");

    EXPECT_EQ(profile.model, SonyModel::WHXB900N);
    EXPECT_EQ(profile.protocol, SonyProtocolVersion::V1);
    EXPECT_TRUE(profile.capabilities.noiseCancelling);
    EXPECT_TRUE(profile.capabilities.ambientSound);
    EXPECT_TRUE(profile.capabilities.equalizer);
    EXPECT_TRUE(profile.capabilities.clearBass);
    EXPECT_TRUE(profile.capabilities.dsee);
    EXPECT_FALSE(profile.capabilities.dseeExtreme);
    EXPECT_TRUE(profile.capabilities.connectionQuality);
    EXPECT_TRUE(profile.capabilities.voiceGuidance);
    EXPECT_TRUE(profile.capabilities.vpt);
    EXPECT_TRUE(profile.capabilities.soundPosition);
    EXPECT_FALSE(profile.capabilities.powerOff);
    EXPECT_FALSE(profile.capabilities.autoPowerOffWhenRemoved);
    EXPECT_TRUE(profile.capabilities.autoPowerOff);
    EXPECT_FALSE(profile.capabilities.wearSensor);
    EXPECT_FALSE(profile.capabilities.multipoint);
}

TEST_F(WhXb900nConnection, ConnectUsesInitHandshakeThenLegacyV1) {
    connect();

    const auto requests = headset->requests();
    ASSERT_GE(requests.size(), 2u);
    EXPECT_EQ(requests[0], (Payload{0x00, 0x00}));
    EXPECT_EQ(requests[1], (Payload{0x66, 0x02}));

    const auto state = controller->state();
    EXPECT_EQ(state.connectionQuality, 0);
    EXPECT_EQ(state.voiceGuidance, 1);
    EXPECT_EQ(state.vpt, 0);
    EXPECT_EQ(state.soundPosition, 0x01);
    EXPECT_TRUE(state.dsee);
    EXPECT_EQ(state.autoPowerOff, 1);
    EXPECT_EQ(state.firmware, "4.5.2");
    EXPECT_EQ(controller->generation(), ProtocolGeneration::V1);
}

TEST_F(WhXb900nConnection, ReconnectRepeatsV2HandshakeBeforeLegacyV1) {
    connect();
    headset->simulateDisconnect();
    ASSERT_TRUE(waitUntil([&] { return !controller->isConnected(); }));

    const auto requestCount = headset->requests().size();
    scriptWhXb900nConnect(*headset);
    controller->connect(kTestAddress);

    const auto requests = headset->requests();
    ASSERT_GE(requests.size(), requestCount + 2);
    EXPECT_EQ(requests[requestCount], (Payload{0x00, 0x00}));
    EXPECT_EQ(requests[requestCount + 1], (Payload{0x66, 0x02}));
    EXPECT_EQ(controller->generation(), ProtocolGeneration::V1);
}

TEST_F(WhXb900nConnection, SetNoiseControlUsesXb900nLayout) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{
        .mode = NoiseControlMode::NoiseCancelling,
        .ambientLevel = 0,
        .focusOnVoice = false,
    });
    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x10, 0x02, 0x01, 0x01, 0x00, 0x00}));

    headset->reply();
    controller->setNoiseControl(NoiseControlState{
        .mode = NoiseControlMode::Ambient,
        .ambientLevel = 15,
        .focusOnVoice = true,
    });
    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x10, 0x02, 0x00, 0x01, 0x01, 0x0f}));

    headset->reply();
    controller->setNoiseControl(NoiseControlState{
        .mode = NoiseControlMode::Off,
        .ambientLevel = 0,
        .focusOnVoice = false,
    });
    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00}));
}

TEST_F(WhXb900nConnection, SetCustomEqualizerUsesXb900nManualWriteValue) {
    connect();

    headset->reply();
    controller->setEqualizerCustom(2, {0, 1, -1, 5, -5});

    EXPECT_EQ(headset->requests().back(),
        (Payload{0x58, 0x01, 0xff, 0x06, 0x0c, 0x0a, 0x0b, 0x09, 0x0f, 0x05}));
}

TEST_F(WhXb900nConnection, DseeAndConnectionQualityRequireConfirmedReplies) {
    connect();

    headset->reply({{0xe9, 0x02, 0x00, 0x00}});
    controller->setDsee(false);
    EXPECT_EQ(headset->requests().back(), (Payload{0xe8, 0x02, 0x00, 0x00}));

    headset->reply({{0xe9, 0x01, 0x00, 0x01}});
    controller->setConnectionQuality(true);  // stable connection
    EXPECT_EQ(headset->requests().back(), (Payload{0xe8, 0x01, 0x00, 0x01}));
    EXPECT_EQ(controller->state().connectionQuality, 1);

    headset->reply({{0xe9, 0x01, 0x00, 0x00}});
    controller->setConnectionQuality(false);  // sound quality
    EXPECT_EQ(headset->requests().back(), (Payload{0xe8, 0x01, 0x00, 0x00}));
    EXPECT_EQ(controller->state().connectionQuality, 0);
}

TEST_F(WhXb900nConnection, RejectedDseeReplyDoesNotChangeConfirmedState) {
    connect();
    ASSERT_TRUE(controller->state().dsee);

    headset->reply({{0xe9, 0x02, 0x00, 0x01}});

    EXPECT_THROW(controller->setDsee(false), SonyException);
    EXPECT_TRUE(controller->state().dsee);
}

TEST_F(WhXb900nConnection, AutoPowerOffUsesCapturedLegacyCommandFamily) {
    connect();

    headset->reply({{0xf9, 0x04, 0x01, 0x03, 0x03}});
    controller->setAutoPowerOff(4);
    EXPECT_EQ(headset->requests().back(), (Payload{0xf8, 0x04, 0x01, 0x03, 0x03}));

    headset->reply({{0xf9, 0x04, 0x01, 0x11, 0x03}});
    controller->setAutoPowerOff(0);
    EXPECT_EQ(headset->requests().back(), (Payload{0xf8, 0x04, 0x01, 0x11, 0x03}));
}

TEST_F(WhXb900nConnection, LegacySpatialAndVoiceGuidanceUseCapturedCommands) {
    connect();

    headset->reply();
    controller->setVpt(3);
    EXPECT_EQ(headset->requests().back(), (Payload{0x48, 0x01, 0x03}));
    EXPECT_EQ(controller->state().vpt, 3);
    EXPECT_EQ(controller->state().soundPosition, 0);

    headset->reply();
    controller->setSoundPosition(0x11);
    EXPECT_EQ(headset->requests().back(), (Payload{0x48, 0x02, 0x11}));
    EXPECT_EQ(controller->state().soundPosition, 0x11);
    EXPECT_EQ(controller->state().vpt, 0);

    headset->replyTable2({{0x49, 0x01, 0x01, 0x00}});
    controller->setVoiceGuidance(0);
    EXPECT_EQ(headset->requests().back(), (Payload{0x48, 0x01, 0x01, 0x00}));
    EXPECT_EQ(headset->requestTypes().back(), DataType::DataMdrNo2);
    EXPECT_EQ(controller->state().voiceGuidance, 0);
}

TEST_F(WhXb900nConnection, RejectsInvalidLegacySpatialValuesWithoutSending) {
    connect();

    const auto requestCount = headset->requests().size();
    const auto initialState = controller->state();

    EXPECT_THROW(controller->setVpt(-1), SonyException);
    EXPECT_THROW(controller->setVpt(5), SonyException);
    EXPECT_THROW(controller->setSoundPosition(-1), SonyException);
    EXPECT_THROW(controller->setSoundPosition(0x10), SonyException);

    EXPECT_EQ(headset->requests().size(), requestCount);
    EXPECT_EQ(controller->state().vpt, initialState.vpt);
    EXPECT_EQ(controller->state().soundPosition, initialState.soundPosition);
}

TEST(WhXb900nNotifications, ParsesLiveCodecChanges) {
    DeviceState state;

    EXPECT_TRUE(applyV1Notification(Payload{0x1b, 0x00, 0x01}, state, true));
    EXPECT_EQ(state.codec, "SBC");

    EXPECT_TRUE(applyV1Notification(Payload{0x1b, 0x00, 0x02}, state, true));
    EXPECT_EQ(state.codec, "AAC");

    EXPECT_TRUE(applyV1Notification(Payload{0x1b, 0x00, 0x20}, state, true));
    EXPECT_EQ(state.codec, "aptX");

    EXPECT_TRUE(applyV1Notification(Payload{0x1b, 0x00, 0x21}, state, true));
    EXPECT_EQ(state.codec, "aptX HD");

    EXPECT_FALSE(applyV1Notification(Payload{0x1b, 0x00, 0xff}, state, true));
    EXPECT_EQ(state.codec, "aptX HD");
}

TEST_F(WhXb900nConnection, LiveCodecNotificationsUpdateOnlyThisModel) {
    connect();
    headset->notify({0x1b, 0x00, 0x02});
    EXPECT_TRUE(waitUntil([&] { return controller->state().codec == "AAC"; }));
}
