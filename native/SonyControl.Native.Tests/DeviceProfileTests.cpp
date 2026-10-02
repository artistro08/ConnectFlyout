#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/ErrorMapping.h"

#include <gtest/gtest.h>

using sony::SonyErrorCode;
using sony::protocol::DeviceProfileRegistry;
using sony::protocol::SonyModel;
using sony::protocol::SonyProtocolVersion;
using sony::protocol::toHresult;

TEST(DeviceProfile, IdentifiesWf1000Xm6) {
    EXPECT_TRUE(DeviceProfileRegistry::identifyModel("WF-1000XM6") == SonyModel::WF1000XM6);
    EXPECT_TRUE(DeviceProfileRegistry::identifyModel("LE_WF-1000XM6") == SonyModel::WF1000XM6);
}

TEST(DeviceProfile, Wf1000Xm6UsesV2WithEarbudBatteries) {
    const auto profile = DeviceProfileRegistry::getProfileForDevice("WF-1000XM6");

    EXPECT_TRUE(profile.protocol == SonyProtocolVersion::V2);
    EXPECT_TRUE(profile.capabilities.dualBattery);
    EXPECT_TRUE(profile.capabilities.dsee);
    EXPECT_TRUE(profile.capabilities.dseeExtreme);
    EXPECT_TRUE(profile.capabilities.speakToChat);
    // A real WF-1000XM6 doesn't answer adaptive volume and has a ten-band EQ without Clear Bass.
    EXPECT_FALSE(profile.capabilities.adaptiveVolume);
    EXPECT_FALSE(profile.capabilities.clearBass);
}

TEST(DeviceProfile, NamesWf1000Xm6) {
    // Upstream fell through to "Unknown" for this model.
    EXPECT_EQ(to_string(SonyModel::WF1000XM6), "WF-1000XM6");
}

TEST(DeviceProfile, DseeBrandingPreservesExistingLabelsThroughCapabilities) {
    EXPECT_FALSE(DeviceProfileRegistry::getProfileForDevice("WH-XB900N").capabilities.dseeExtreme);
    EXPECT_TRUE(DeviceProfileRegistry::getProfileForDevice("WH-CH720N").capabilities.dseeExtreme);
    EXPECT_TRUE(DeviceProfileRegistry::getProfileForDevice("ULT WEAR").capabilities.dseeExtreme);
    EXPECT_TRUE(DeviceProfileRegistry::getProfileForDevice("WH-1000XM5").capabilities.dseeExtreme);
    EXPECT_TRUE(DeviceProfileRegistry::getProfileForDevice("LinkBuds S").capabilities.dseeExtreme);
}

TEST(DeviceProfile, UnknownNamesFallBackToUnknownModel) {
    EXPECT_TRUE(DeviceProfileRegistry::identifyModel("Galaxy Buds") == SonyModel::Unknown);

    const auto profile = DeviceProfileRegistry::getProfileForDevice("Galaxy Buds");
    EXPECT_TRUE(profile.capabilities.powerOff);
    EXPECT_TRUE(profile.capabilities.autoPowerOffWhenRemoved);
}

TEST(ErrorMapping, MapsEveryCodeToItsHresult) {
    EXPECT_EQ(toHresult(SonyErrorCode::Timeout), static_cast<int32_t>(0x800705B4));
    EXPECT_EQ(toHresult(SonyErrorCode::Disconnected), static_cast<int32_t>(0x8007048F));
    EXPECT_EQ(toHresult(SonyErrorCode::Unsupported), static_cast<int32_t>(0x80004001));
    EXPECT_EQ(toHresult(SonyErrorCode::InvalidFrame), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::InvalidChecksum), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::InvalidResponse), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::ProtocolViolation), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::TransportFailure), static_cast<int32_t>(0x800704C9));
}

TEST(DeviceProfile, UltWearUsesUltEqualizerLayout) {
    EXPECT_TRUE(DeviceProfileRegistry::getProfileForDevice("ULT WEAR").capabilities.ultEqualizer);
    EXPECT_FALSE(DeviceProfileRegistry::getProfileForDevice("WH-CH720N").capabilities.ultEqualizer);
}
