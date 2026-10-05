#include "sony/protocol/FrameCodec.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <functional>

using sony::SonyErrorCode;
using sony::SonyException;
using sony::protocol::DataType;
using sony::protocol::FrameCodec;
using sony::protocol::SonyFrame;

namespace {

using Bytes = std::vector<uint8_t>;

SonyErrorCode errorCodeOf(const std::function<void()>& action) {
    try {
        action();
    } catch (const SonyException& ex) {
        return ex.code();
    }
    ADD_FAILURE() << "Expected a SonyException";
    return SonyErrorCode::ProtocolViolation;
}

} // namespace

TEST(FrameCodec, EncodesHandshakeFrame) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x00, 0x00}};

    // 3e | type 0c | seq 00 | length 00 00 00 02 | payload 00 00 | checksum 0e | 3c
    const Bytes expected{0x3e, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x0e, 0x3c};
    EXPECT_EQ(FrameCodec::encode(frame), expected);
}

TEST(FrameCodec, RoundTripsAFrame) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 1, .payload = {0x68, 0x17, 0x01, 0x01, 0x01, 0x01, 0x08}};

    EXPECT_EQ(FrameCodec::decode(FrameCodec::encode(frame)), frame);
}

TEST(FrameCodec, EscapesAllThreeMarkerBytes) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x3c, 0x3d, 0x3e}};
    const Bytes encoded = FrameCodec::encode(frame);

    const Bytes escapedPayload{0x3d, 0x2c, 0x3d, 0x2d, 0x3d, 0x2e};
    EXPECT_NE(std::search(encoded.begin(), encoded.end(), escapedPayload.begin(), escapedPayload.end()), encoded.end());
    EXPECT_EQ(FrameCodec::decode(encoded), frame);
}

TEST(FrameCodec, ChecksumIsByteSumModulo256) {
    const Bytes data{0xff, 0x02};
    EXPECT_EQ(FrameCodec::calculateChecksum(data), 0x01);
}

TEST(FrameCodec, WritesLengthBigEndian) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 0, .payload = Bytes(300, 0x01)};
    const Bytes encoded = FrameCodec::encode(frame);

    EXPECT_EQ(encoded[3], 0x00);
    EXPECT_EQ(encoded[4], 0x00);
    EXPECT_EQ(encoded[5], 0x01);
    EXPECT_EQ(encoded[6], 0x2c);
}

TEST(FrameCodec, RejectsBadChecksum) {
    Bytes encoded = FrameCodec::encode(SonyFrame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x00, 0x00}});
    encoded[encoded.size() - 2] = 0x0f;

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidChecksum);
}

TEST(FrameCodec, RejectsUnknownEscapeSequence) {
    const Bytes encoded{0x3e, 0x0c, 0x3d, 0x99, 0x00, 0x00, 0x00, 0x00, 0x0c, 0x3c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}

TEST(FrameCodec, RejectsTruncatedFrame) {
    const Bytes encoded{0x3e, 0x0c, 0x00, 0x3c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}

TEST(FrameCodec, RejectsLengthLongerThanData) {
    const Bytes encoded{0x3e, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x15, 0x3c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}

TEST(FrameCodec, RejectsMissingDelimiters) {
    const Bytes encoded{0x0c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}
