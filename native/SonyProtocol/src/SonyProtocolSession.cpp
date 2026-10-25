#include "sony/protocol/SonyProtocolSession.h"
#include "sony/transport/Logger.h"
#include <algorithm>
#include <array>

namespace sony::protocol {

SonyProtocolSession::SonyProtocolSession(std::unique_ptr<transport::ITransport> transport)
    : _ownedTransport(std::move(transport)), _transport(_ownedTransport.get()) {}

SonyProtocolSession::SonyProtocolSession(transport::ITransport* transport)
    : _transport(transport) {}

SonyProtocolSession::~SonyProtocolSession() {
    disconnect();
}

void SonyProtocolSession::connect(const transport::DeviceAddress& address) {
    if (isConnected() || _running.load()) {
        disconnect();
    }
    if (!_transport) {
        throw SonyException(SonyErrorCode::TransportFailure, "No transport configured");
    }
    // Addresses only at debug: logs get attached to bug reports
    Logger::debug(LogCategory::Transport, "Connecting transport to " + address.str());
    _transport->connect(address);
    Logger::info(LogCategory::Transport, "Transport connected");
    start();
}

void SonyProtocolSession::start() {
    if (!_transport || !_transport->isConnected()) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport is not connected");
    }
    if (_running.load()) {
        return;
    }
    {
        std::lock_guard lock(_sessionMtx);
        _streamBuffer.clear();
        _lastSeenDataSeq.reset();
        _hasAck = false;
        _pendingRequest.reset();
    }
    _startReader();
}

void SonyProtocolSession::disconnect() noexcept {
    // Close the link before joining the reader: closing wakes a blocked
    // receive right away instead of after its 2.5 s timeout.
    _stopRequested.store(true);
    _running.store(false);
    if (_transport) {
        _transport->disconnect();
    }
    _stopReader();
    {
        std::lock_guard lock(_sessionMtx);
        _streamBuffer.clear();
        _lastSeenDataSeq.reset();
        _pendingRequest.reset();
        _hasAck = false;
        _sequence.store(0);
        _ackCv.notify_all();
        _responseCv.notify_all();
    }
}

bool SonyProtocolSession::isConnected() const noexcept {
    return _running.load() && _transport && _transport->isConnected();
}

transport::ITransport* SonyProtocolSession::transport() const noexcept {
    return _transport;
}

void SonyProtocolSession::_startReader() {
    _stopReader();
    _stopRequested.store(false);
    _running.store(true);
    _readerThread = std::thread(&SonyProtocolSession::_readerLoop, this);
}

void SonyProtocolSession::_stopReader() noexcept {
    _stopRequested.store(true);
    _running.store(false);
    {
        std::lock_guard lock(_sessionMtx);
        _ackCv.notify_all();
        _responseCv.notify_all();
    }
    if (_readerThread.joinable()) {
        if (std::this_thread::get_id() != _readerThread.get_id()) {
            _readerThread.join();
        } else {
            // Called from a callback on the reader thread: it can't join itself.
            _readerThread.detach();
        }
    }
}

void SonyProtocolSession::_readerLoop() {
    std::array<std::byte, 1024> recvBuf;
    while (_running.load()) {
        size_t bytesRead = 0;
        try {
            bytesRead = _transport->receive(recvBuf);
            if (bytesRead == 0) {
                break;
            }
        } catch (const SonyException& ex) {
            if (ex.code() == SonyErrorCode::Timeout) {
                if (!_running.load()) {
                    break;
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(2));
                continue;
            }
            break;
        } catch (const std::exception&) {
            break;
        }

        _handleIncomingBytes(std::span<const std::byte>(recvBuf.data(), bytesRead));
    }

    _running.store(false);
    DisconnectedCallback disconnectedCallback;
    {
        std::lock_guard lock(_sessionMtx);
        _ackCv.notify_all();
        _responseCv.notify_all();
        if (!_stopRequested.load()) {
            disconnectedCallback = _disconnectedCallback;
        }
    }

    if (!_stopRequested.load()) {
        Logger::info(LogCategory::Session, "Link dropped");
    }
    if (disconnectedCallback) {
        try {
            disconnectedCallback();
        } catch (...) {
            // Suppress exceptions from client callbacks
        }
    }
}

void SonyProtocolSession::_handleIncomingBytes(std::span<const std::byte> bytes) {
    std::vector<SonyFrame> framesToProcess;
    {
        std::lock_guard lock(_sessionMtx);
        const auto* data = reinterpret_cast<const uint8_t*>(bytes.data());
        _streamBuffer.insert(_streamBuffer.end(), data, data + bytes.size());
        _parseStream(framesToProcess);
    }

    for (const auto& frame : framesToProcess) {
        _handleDecodedFrame(frame);
    }
}

size_t SonyProtocolSession::bufferedByteCount() const {
    std::lock_guard lock(_sessionMtx);
    return _streamBuffer.size();
}

void SonyProtocolSession::_parseStream(std::vector<SonyFrame>& outFrames) {
    while (true) {
        auto startIt = std::find(_streamBuffer.begin(), _streamBuffer.end(), FrameCodec::START_MARKER);
        if (startIt == _streamBuffer.end()) {
            _streamBuffer.clear();
            return;
        }

        if (startIt != _streamBuffer.begin()) {
            _streamBuffer.erase(_streamBuffer.begin(), startIt);
            startIt = _streamBuffer.begin();
        }

        auto endIt = _streamBuffer.end();
        for (auto it = _streamBuffer.begin() + 1; it != _streamBuffer.end(); ++it) {
            if (*it == FrameCodec::START_MARKER) {
                startIt = it;
                break;
            }
            if (*it == FrameCodec::END_MARKER) {
                endIt = it;
                break;
            }
        }

        if (startIt != _streamBuffer.begin()) {
            _streamBuffer.erase(_streamBuffer.begin(), startIt);
            continue;
        }

        // No frame end yet. A frame that has run past twice the largest frame (escaping at most
        // doubles it) can't be real: drop it whole, so a device that never ends a frame can't
        // grow the buffer without limit. The next start marker resyncs.
        if (endIt == _streamBuffer.end()) {
            if (_streamBuffer.size() > FrameCodec::MAX_FRAME_SIZE * 2) {
                Logger::debug(LogCategory::Session, "Dropped " + std::to_string(_streamBuffer.size()) + " bytes without a frame end");
                _streamBuffer.clear();
            }
            return;
        }

        size_t frameLen = std::distance(_streamBuffer.begin(), endIt) + 1;
        std::vector<uint8_t> frameSlice(_streamBuffer.begin(), _streamBuffer.begin() + frameLen);
        _streamBuffer.erase(_streamBuffer.begin(), _streamBuffer.begin() + frameLen);

        try {
            SonyFrame decoded = FrameCodec::decode(frameSlice);
            outFrames.push_back(std::move(decoded));
        } catch (const SonyException& ex) {
            // Corrupt frame: dropped without an ACK so the headset resends it
            Logger::debug(LogCategory::Session, std::string("Dropped corrupt frame (") + ex.what() + "): " + Logger::formatHex(frameSlice));
        } catch (const std::exception&) {
            // Corrupt frame: dropped without crash, continue loop
        }
    }
}

void SonyProtocolSession::_handleDecodedFrame(const SonyFrame& frame) {
    if (frame.type == DataType::Ack) {
        Logger::debug(LogCategory::Session, "RX  ACK seq=" + std::to_string(frame.sequence));
        std::lock_guard lock(_sessionMtx);
        _hasAck = true;
        if (frame.sequence <= 1) {
            _sequence.store(frame.sequence);
        }
        if (_pendingRequest) {
            _pendingRequest->hasAck = true;
        }
        _ackCv.notify_all();
        _responseCv.notify_all();
        return;
    }

    // Both command tables: the main one and the second (DataMdrNo2, multipoint and power)
    if (frame.type == DataType::DataMdr || frame.type == DataType::DataMdrNo2) {
        std::string desc = Logger::describePayload(frame.payload);
        if (!desc.empty()) {
            Logger::debug(LogCategory::Protocol, desc);
        }

        // Send ACK back to device with toggled 1-bit sequence
        try {
            _sendAck(static_cast<uint8_t>(1 - (frame.sequence & 1)));
        } catch (...) {
            // Transport might have dropped
        }

        // Check duplicate frame
        bool isDuplicate = false;
        {
            std::lock_guard lock(_sessionMtx);
            if (_lastSeenDataSeq.has_value() && *_lastSeenDataSeq == frame.sequence) {
                isDuplicate = true;
            } else {
                _lastSeenDataSeq = frame.sequence;
            }
        }
        if (isDuplicate) {
            return;
        }

        // Check if matching pending request
        bool matchedRequest = false;
        {
            std::lock_guard lock(_sessionMtx);
            if (_pendingRequest && !_pendingRequest->hasResponse) {
                if (frame.type == _pendingRequest->expectedType && !frame.payload.empty() &&
                    frame.payload[0] == _pendingRequest->expectedOpcode) {
                    if (_pendingRequest->expectedSubtype < 0 ||
                        (frame.payload.size() >= 2 && frame.payload[1] == static_cast<uint8_t>(_pendingRequest->expectedSubtype)))
                    {
                        _pendingRequest->response = frame;
                        _pendingRequest->hasResponse = true;
                        matchedRequest = true;
                        _responseCv.notify_all();
                    }
                }
            }
        }

        if (matchedRequest) {
            return;
        }

        // Unsolicited notification dispatch
        std::vector<NotificationCallback> callbacks;
        {
            std::lock_guard lock(_sessionMtx);
            callbacks = _notificationCallbacks;
        }
        for (const auto& cb : callbacks) {
            if (cb) {
                try {
                    cb(frame);
                } catch (...) {
                    // Suppress exceptions from client callbacks
                }
            }
        }
    }
}

void SonyProtocolSession::_sendAck(uint8_t seqNumber) {
    Logger::debug(LogCategory::Session, "TX  ACK seq=" + std::to_string(seqNumber));
    SonyFrame ackFrame{
        .type = DataType::Ack,
        .sequence = seqNumber,
        .payload = {}
    };
    _writeFrame(ackFrame);
}

void SonyProtocolSession::_writeFrame(const SonyFrame& frame) {
    std::lock_guard lock(_writeMtx);
    if (!_transport || !_transport->isConnected()) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport not connected");
    }
    auto encoded = FrameCodec::encode(frame);
    Logger::logTx(encoded, Logger::describePayload(frame.payload));
    std::span<const std::byte> byteSpan(reinterpret_cast<const std::byte*>(encoded.data()), encoded.size());
    try {
        size_t sent = 0;
        while (sent < byteSpan.size()) {
            auto n = _transport->send(byteSpan.subspan(sent));
            if (!n) throw SonyException(SonyErrorCode::Disconnected, "Bluetooth send returned EOF");
            sent += n;
        }
    } catch (const SonyException& ex) {
        if (ex.code() != SonyErrorCode::Timeout) {
            _running = false; _ackCv.notify_all(); _responseCv.notify_all();
        }
        throw;
    } catch (...) {
        _running = false; _ackCv.notify_all(); _responseCv.notify_all(); throw;
    }
}

void SonyProtocolSession::send(const SonyFrame& frame, std::chrono::milliseconds timeout) {
    if (!isConnected()) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport not connected");
    }

    std::unique_lock sendLock(_sendMtx);

    SonyFrame toSend = frame;
    // Both command tables share one sequence
    if (toSend.type == DataType::DataMdr || toSend.type == DataType::DataMdrNo2) {
        toSend.sequence = nextSequenceNumber();
    }

    {
        std::lock_guard lock(_sessionMtx);
        _expectedAckSeq = toSend.sequence;
        _hasAck = false;
    }

    _writeFrame(toSend);

    auto deadline = std::chrono::steady_clock::now() + timeout;
    std::unique_lock lock(_sessionMtx);
    bool received = _ackCv.wait_until(lock, deadline, [this] {
        return !_running.load() || !isConnected() || _hasAck;
    });

    if (!isConnected() || !_running.load()) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport disconnected while waiting for ACK");
    }
    if (!received || !_hasAck) {
        throw SonyException(SonyErrorCode::Timeout, "Timeout waiting for ACK");
    }
    _hasAck = false;
}

SonyFrame SonyProtocolSession::sendAndAwaitResponse(
    const SonyFrame& request,
    uint8_t retOpcode,
    int retSubtype,
    std::chrono::milliseconds timeout)
{
    if (!isConnected()) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport not connected");
    }

    std::unique_lock sendLock(_sendMtx);

    SonyFrame toSend = request;
    // Both command tables share one sequence
    if (toSend.type == DataType::DataMdr || toSend.type == DataType::DataMdrNo2) {
        toSend.sequence = nextSequenceNumber();
    }

    {
        std::lock_guard lock(_sessionMtx);
        _expectedAckSeq = toSend.sequence;
        _hasAck = false;
        _pendingRequest = PendingRequest{
            .expectedType = request.type,
            .expectedOpcode = retOpcode,
            .expectedSubtype = retSubtype,
            .hasResponse = false,
            .response = {},
            .hasAck = false,
            .expectedAckSeq = toSend.sequence
        };
        // No match against frames that arrived earlier: the request is registered before it is
        // written, so anything already received is an older notification, not this answer
    }

    _writeFrame(toSend);

    auto deadline = std::chrono::steady_clock::now() + timeout;
    std::unique_lock lock(_sessionMtx);
    bool received = _responseCv.wait_until(lock, deadline, [this] {
        return !_running.load() || !isConnected() || (_pendingRequest && _pendingRequest->hasResponse);
    });

    if (!isConnected() || !_running.load()) {
        _pendingRequest.reset();
        throw SonyException(SonyErrorCode::Disconnected, "Transport disconnected while waiting for response");
    }
    if (!received || !_pendingRequest || !_pendingRequest->hasResponse) {
        _pendingRequest.reset();
        throw SonyException(SonyErrorCode::Timeout, "Timeout waiting for response from device");
    }

    _hasAck = false;
    SonyFrame resp = std::move(_pendingRequest->response);
    _pendingRequest.reset();
    return resp;
}

void SonyProtocolSession::onNotification(NotificationCallback callback) {
    std::lock_guard lock(_sessionMtx);
    _notificationCallbacks.push_back(std::move(callback));
}

void SonyProtocolSession::onDisconnected(DisconnectedCallback callback) {
    std::lock_guard lock(_sessionMtx);
    _disconnectedCallback = std::move(callback);
}

uint8_t SonyProtocolSession::nextSequenceNumber() noexcept {
    uint8_t cur = _sequence.load();
    if (cur > 1 && cur < 254) {
        cur = cur & 1;
        _sequence.store(cur);
    }
    return _sequence.fetch_add(1);
}

uint8_t SonyProtocolSession::currentSequenceNumber() const noexcept {
    return _sequence.load();
}

void SonyProtocolSession::setSequenceNumber(uint8_t seq) noexcept {
    _sequence.store(seq);
}

} // namespace sony::protocol
