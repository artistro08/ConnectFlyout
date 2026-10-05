// WH-1000XM4 (protocol V1) tests. Every suite name starts with "Xm4" so the
// first build can leave them out with --gtest_filter=-Xm4*.

#include "FakeHeadset.h"

#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/HeadsetController.h"
#include "sony/protocol/V1Notifications.h"

#include <gtest/gtest.h>

#include <memory>

using sony::protocol::DeviceProfileRegistry;
using sony::protocol::DeviceState;
using sony::protocol::HeadsetController;
using sony::protocol::NoiseControlMode;
using sony::protocol::NoiseControlState;
using sony::protocol::SonyModel;
using sony::protocol::SonyProtocolVersion;
using sony::protocol::applyV1Notification;
using sony::test::FakeHeadset;
using sony::test::kTestAddress;
using sony::test::Payload;
using sony::test::scriptXm4Connect;
using sony::test::waitUntil;

namespace {

class Xm4Connection : public ::testing::Test {
protected:
    void SetUp() override {
        auto transport = std::make_unique<FakeHeadset>();
        headset = transport.get();
        controller = std::make_unique<HeadsetController>(std::move(transport), "WH-1000XM4");
    }

    void connect() {
        scriptXm4Connect(*headset);
        controller->connect(kTestAddress);
    }

    FakeHeadset* headset{};
    std::unique_ptr<HeadsetController> controller;
};

} // namespace

TEST(Xm4Profile, UsesV1WithSingleBattery) {
    const auto profile = DeviceProfileRegistry::getProfileForDevice("WH-1000XM4");

    EXPECT_TRUE(profile.model == SonyModel::WH1000XM4);
    EXPECT_TRUE(profile.protocol == SonyProtocolVersion::V1);
    EXPECT_FALSE(profile.capabilities.dualBattery);
    EXPECT_FALSE(profile.capabilities.dsee);
    EXPECT_TRUE(profile.capabilities.powerOff);
    EXPECT_TRUE(profile.capabilities.autoPowerOffWhenRemoved);
}

TEST_F(Xm4Connection, ConnectReadsV1State) {
    connect();

    ASSERT_TRUE(controller->isConnected());
    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.main.value_or(-1), 60);
    EXPECT_FALSE(state.battery.left.has_value());
    EXPECT_TRUE(state.noiseControl.mode == NoiseControlMode::Ambient);
    EXPECT_EQ(state.noiseControl.ambientLevel, 15);
    EXPECT_TRUE(state.noiseControl.focusOnVoice);
    EXPECT_EQ(state.equalizer.preset, 0x16);
    EXPECT_EQ(state.equalizer.clearBass, 2);
    EXPECT_EQ(state.firmware, "3.0.1");
    EXPECT_EQ(state.codec, "AAC");
}

TEST_F(Xm4Connection, NeverSendsThePowerOffOpcode) {
    connect();
    headset->reply({{0x11, 0x00, 58, 0x00}});
    controller->refreshBattery();

    for (const auto& request : headset->requests()) {
        ASSERT_FALSE(request.empty());
        EXPECT_NE(request.front(), 0x22);
    }
}

TEST_F(Xm4Connection, PowerOffSendsTheV1PowerOffCommand) {
    connect();

    headset->reply();
    controller->powerOff();

    EXPECT_EQ(headset->requests().back(), (Payload{0x22, 0x00, 0x01}));
}

TEST_F(Xm4Connection, SetAmbientSendsV1Bytes) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::Ambient, .ambientLevel = 15, .focusOnVoice = true});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x11, 0x01, 0x00, 0x01, 0x01, 0x0f}));
}

TEST_F(Xm4Connection, SetNoiseCancellingSendsV1Bytes) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::NoiseCancelling, .ambientLevel = 0, .focusOnVoice = false});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x11, 0x01, 0x02, 0x01, 0x00, 0x00}));
}

TEST_F(Xm4Connection, V1NotificationUpdatesState) {
    connect();

    headset->notify({0x69, 0x02, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00});

    EXPECT_TRUE(waitUntil([&] { return controller->state().noiseControl.mode == NoiseControlMode::Off; }));
}

TEST(Xm4Notifications, ParsesBattery) {
    DeviceState state;
    const Payload payload{0x13, 0x00, 40, 0x01};

    EXPECT_TRUE(applyV1Notification(payload, state));
    EXPECT_EQ(state.battery.main.value_or(-1), 40);
    EXPECT_TRUE(state.battery.charging);
}

TEST(Xm4Notifications, ParsesEqualizer) {
    DeviceState state;
    const Payload payload{0x59, 0x01, 0xa0, 0x06, 0x0a, 0x00, 0x05, 0x0a, 0x0f, 0x14};

    EXPECT_TRUE(applyV1Notification(payload, state));
    EXPECT_EQ(state.equalizer.preset, 0xa0);
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{-10, -5, 0, 5, 10}));
}

// A device left on "Custom 3" (0xa2) still folds to Manual (0xa0).
TEST(Xm4Notifications, FoldsOtherCustomSlotsIntoManual) {
    DeviceState state;
    const Payload payload{0x59, 0x01, 0xa2, 0x06, 0x0a, 0x00, 0x05, 0x0a, 0x0f, 0x14};

    EXPECT_TRUE(applyV1Notification(payload, state));
    EXPECT_EQ(state.equalizer.preset, 0xa0);
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{-10, -5, 0, 5, 10}));
}

TEST(Xm4Notifications, IgnoresWhXb900nCodecNotification) {
    DeviceState state;

    EXPECT_FALSE(applyV1Notification(Payload{0x1b, 0x00, 0x02}, state));
    EXPECT_TRUE(state.codec.empty());
}

TEST(Xm4Notifications, IgnoresV2Layouts) {
    DeviceState state;
    const Payload payload{0x69, 0x17, 0x01, 0x01, 0x00, 0x00, 0x00};

    EXPECT_FALSE(applyV1Notification(payload, state));
}
