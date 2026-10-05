#include "sony/transport/BluetoothAddress.h"

#include <gtest/gtest.h>

using sony::transport::parseBluetoothAddress;

TEST(BluetoothAddress, ParsesColonSeparated) {
    EXPECT_EQ(parseBluetoothAddress("AA:BB:CC:DD:EE:FF").value_or(0), 0xAABBCCDDEEFFULL);
}

TEST(BluetoothAddress, ParsesLowercaseWithDashes) {
    EXPECT_EQ(parseBluetoothAddress("ac-80-0a-12-34-56").value_or(0), 0xAC800A123456ULL);
}

TEST(BluetoothAddress, ParsesBareHex) {
    EXPECT_EQ(parseBluetoothAddress("AC800A123456").value_or(0), 0xAC800A123456ULL);
}

TEST(BluetoothAddress, RejectsTooFewDigits) {
    EXPECT_FALSE(parseBluetoothAddress("AA:BB:CC:DD:EE").has_value());
}

TEST(BluetoothAddress, RejectsTooManyDigits) {
    EXPECT_FALSE(parseBluetoothAddress("AA:BB:CC:DD:EE:FF:00").has_value());
}

TEST(BluetoothAddress, RejectsNonHexDigits) {
    EXPECT_FALSE(parseBluetoothAddress("ZZ:BB:CC:DD:EE:FF").has_value());
}

TEST(BluetoothAddress, RejectsEmptyText) {
    EXPECT_FALSE(parseBluetoothAddress("").has_value());
}
