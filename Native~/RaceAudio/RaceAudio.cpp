#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <mutex>
#include <vector>

#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

// Each Unity source is a single producer. Each device callback is a single consumer.
// Native callbacks allocate nothing and never take a lock.
constexpr int StreamCount = 5; // BGM + two human engines + two CPU engines, per output device.
constexpr ma_uint32 RingFrames = 32768;
constexpr ma_uint32 MixChunkFrames = 256;
struct Stream {
    ma_pcm_rb ring{};
    ma_linear_resampler resampler{};
    std::atomic<ma_uint32> targetFrames{0};
    bool primed = false; // Consumer-owned state below this line.
    float filteredFrames = 0;
    float lastLeft = 0, lastRight = 0;
    float fade = 0;
    std::atomic<ma_uint64> underruns{0};
};
struct Output {
    std::mutex producerMutex;
    ma_device device{};
    Stream streams[StreamCount]{};
    ma_uint32 sampleRate = 48000;
    std::atomic<ma_uint32> minimumBufferMilliseconds{50};
    bool open = false;
};
static ma_context context;
static bool initialized = false;
static std::vector<ma_device_info> devices;
static Output outputs[2];

static bool InitStream(Stream& stream, ma_uint32 sampleRate, ma_uint32 minimumBufferMilliseconds = 50) {
    if (ma_pcm_rb_init(ma_format_f32, 2, RingFrames, nullptr, nullptr, &stream.ring) != MA_SUCCESS) return false;
    auto config = ma_linear_resampler_config_init(ma_format_f32, 2, sampleRate, sampleRate);
    config.lpfOrder = 0; // Only compensate small independent device clock differences.
    if (ma_linear_resampler_init(&config, nullptr, &stream.resampler) != MA_SUCCESS) {
        ma_pcm_rb_uninit(&stream.ring);
        return false;
    }
    stream.targetFrames.store(std::min(RingFrames / 2, sampleRate * minimumBufferMilliseconds / 1000));
    stream.primed = false;
    stream.fade = 0;
    stream.lastLeft = stream.lastRight = 0;
    stream.underruns = 0;
    return true;
}

// Both threads may raise this watermark. The producer must never undo the
// consumer's learned protection on the very next DSP block.
static void RaiseTarget(Stream& stream, ma_uint32 frames) {
    ma_uint32 current = stream.targetFrames.load(std::memory_order_relaxed);
    while (current < frames && !stream.targetFrames.compare_exchange_weak(
        current, frames, std::memory_order_relaxed)) {}
}

static void UninitStream(Stream& stream) {
    ma_linear_resampler_uninit(&stream.resampler, nullptr);
    ma_pcm_rb_uninit(&stream.ring);
}

static void Mix(Output& output, float* data, ma_uint32 frames) {
    std::fill(data, data + frames * 2, 0.0f);
    float scratch[MixChunkFrames * 2];
    for (auto& stream : output.streams) {
        ma_uint32 queued = ma_pcm_rb_available_read(&stream.ring);
        ma_uint32 target = stream.targetFrames.load(std::memory_order_relaxed);
        if (!stream.primed && queued >= target) {
            stream.primed = true;
            stream.filteredFrames = static_cast<float>(target);
            ma_linear_resampler_reset(&stream.resampler);
        }
        ma_uint32 offset = 0;
        if (stream.primed) {
            // Filter callback/block jitter before correcting the independent USB/HDMI
            // clock. A fixed-rate drain eventually empties or fills even a large ring.
            float smoothing = std::min(1.0f, static_cast<float>(frames) / (output.sampleRate * .25f));
            stream.filteredFrames += (queued - stream.filteredFrames) * smoothing;
            float correction = (stream.filteredFrames - target) / output.sampleRate * .1f;
            float ratio = 1.0f + std::max(-.005f, std::min(.005f, correction));
            ma_linear_resampler_set_rate_ratio(&stream.resampler, ratio);
            while (offset < frames) {
                ma_uint32 available = ma_pcm_rb_available_read(&stream.ring);
                void* samples = nullptr;
                ma_pcm_rb_acquire_read(&stream.ring, &available, &samples);
                if (available == 0) break;
                ma_uint64 consumed = available;
                ma_uint64 produced = std::min(MixChunkFrames, frames - offset);
                ma_linear_resampler_process_pcm_frames(&stream.resampler, samples, &consumed, scratch, &produced);
                ma_pcm_rb_commit_read(&stream.ring, static_cast<ma_uint32>(consumed));
                for (ma_uint32 i = 0; i < produced; ++i) {
                    stream.fade = std::min(1.0f, stream.fade + 1.0f / 128);
                    stream.lastLeft = scratch[i * 2] * stream.fade;
                    stream.lastRight = scratch[i * 2 + 1] * stream.fade;
                    data[(offset + i) * 2] += stream.lastLeft;
                    data[(offset + i) * 2 + 1] += stream.lastRight;
                }
                offset += static_cast<ma_uint32>(produced);
                if (consumed == 0 && produced == 0) break;
            }
            if (offset < frames) {
                ++stream.underruns;
                // A 3-block queue does not cover Editor/OS scheduling stalls.
                // Learn from starvation, without growing unbounded latency or
                // trying to disguise a missing block by slowing music down.
                RaiseTarget(stream, std::min(RingFrames / 2,
                    std::min(output.sampleRate / 5, target + output.sampleRate / 25)));
                stream.primed = false; // Refill once, instead of alternating PCM/silence every callback.
                stream.fade = 0;
            }
        }
        // Ramp the last sample to silence on starvation; never loop stale PCM.
        float left = stream.lastLeft, right = stream.lastRight;
        for (ma_uint32 i = 0; offset < frames && i < 128; ++i, ++offset) {
            float gain = 1.0f - (i + 1) / 128.0f;
            data[offset * 2] += left * gain;
            data[offset * 2 + 1] += right * gain;
            stream.lastLeft = left * gain;
            stream.lastRight = right * gain;
        }
    }
    for (ma_uint32 i = 0; i < frames * 2; ++i) data[i] = std::max(-1.0f, std::min(1.0f, data[i]));
}

static void Callback(ma_device* device, void* data, const void*, ma_uint32 frames) {
    Mix(*static_cast<Output*>(device->pUserData), static_cast<float*>(data), frames);
}

static void Close(Output& output) {
    if (!output.open) return;
    output.open = false;
    ma_device_uninit(&output.device); // Joins the device callback before freeing rings.
    for (auto& stream : output.streams) UninitStream(stream);
}

EXPORT int ra_refresh_devices() {
    if (!initialized) {
#ifdef _WIN32
        const ma_backend backend = ma_backend_wasapi;
#elif defined(__APPLE__)
        const ma_backend backend = ma_backend_coreaudio;
#else
        const ma_backend backend = ma_backend_pulseaudio;
#endif
        if (ma_context_init(&backend, 1, nullptr, &context) != MA_SUCCESS) return -1;
        initialized = true;
    }
    ma_device_info* playback = nullptr;
    ma_uint32 count = 0;
    if (ma_context_get_devices(&context, &playback, &count, nullptr, nullptr) != MA_SUCCESS) return -1;
    devices.assign(playback, playback + count);
    return static_cast<int>(count);
}

EXPORT const char* ra_device_name(int index) {
    return index >= 0 && index < static_cast<int>(devices.size()) ? devices[index].name : "";
}

EXPORT const char* ra_device_key(int index) {
    if (index < 0 || index >= static_cast<int>(devices.size())) return "";
#ifdef _WIN32
    // WASAPI endpoint IDs stay stable even if enumeration order or display name changes.
    static thread_local char key[1024];
    WideCharToMultiByte(CP_UTF8, 0, devices[index].id.wasapi, -1, key, sizeof(key), nullptr, nullptr);
    return key;
#elif defined(__APPLE__)
    return devices[index].id.coreaudio;
#else
    return devices[index].name;
#endif
}

EXPORT void ra_set_min_buffer_milliseconds(int player, int milliseconds) {
    if (player < 0 || player > 1) return;
    outputs[player].minimumBufferMilliseconds.store(
        static_cast<ma_uint32>(std::max(50, std::min(150, milliseconds))), std::memory_order_relaxed);
}

EXPORT ma_uint64 ra_get_underruns(int player) {
    if (player < 0 || player > 1) return 0;
    ma_uint64 count = 0;
    for (auto& stream : outputs[player].streams)
        count += stream.underruns.load(std::memory_order_relaxed);
    return count;
}

EXPORT int ra_open(int player, int deviceIndex, int sampleRate) {
    if (player < 0 || player > 1 || !initialized || deviceIndex < 0 ||
        deviceIndex >= static_cast<int>(devices.size()) || sampleRate <= 0) return -1;
    auto& output = outputs[player];
    std::lock_guard<std::mutex> lock(output.producerMutex);
    Close(output);
    output.sampleRate = static_cast<ma_uint32>(sampleRate);
    int ringCount = 0;
    for (auto& stream : output.streams) {
        if (!InitStream(stream, output.sampleRate, output.minimumBufferMilliseconds.load(std::memory_order_relaxed))) {
            for (int i = 0; i < ringCount; ++i) UninitStream(output.streams[i]);
            return -2;
        }
        ++ringCount;
    }
    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.playback.pDeviceID = &devices[deviceIndex].id;
    config.playback.format = ma_format_f32;
    config.playback.channels = 2;
    config.sampleRate = static_cast<ma_uint32>(sampleRate);
    // Prefer a stable device period over minimum latency on USB/DisplayLink docks.
    config.performanceProfile = ma_performance_profile_conservative;
    config.periodSizeInMilliseconds = 10;
    config.periods = 3;
    config.dataCallback = Callback;
    config.pUserData = &output;
    ma_result result = ma_device_init(&context, &config, &output.device);
    if (result != MA_SUCCESS) {
        for (auto& stream : output.streams) UninitStream(stream);
        return result;
    }
    output.open = true;
    result = ma_device_start(&output.device);
    if (result != MA_SUCCESS) Close(output);
    return result;
}

EXPORT int ra_is_started(int player) {
    return player >= 0 && player < 2 && outputs[player].open && ma_device_is_started(&outputs[player].device);
}

EXPORT void ra_push(int player, int streamIndex, const float* data, int frames, int channels,
                    float leftGain, float rightGain) {
    if (player < 0 || player > 1 || streamIndex < 0 || streamIndex >= StreamCount ||
        !data || frames <= 0 || channels <= 0) return;
    auto& output = outputs[player];
    std::unique_lock<std::mutex> lock(output.producerMutex, std::try_to_lock);
    if (!lock.owns_lock() || !output.open) return;
    auto& state = output.streams[streamIndex];
    // Keep at least three Unity DSP blocks, within a bounded latency budget.
    ma_uint32 target = std::min(RingFrames / 2,
        std::max(output.sampleRate * output.minimumBufferMilliseconds.load(std::memory_order_relaxed) / 1000,
            static_cast<ma_uint32>(std::min(frames, static_cast<int>(RingFrames)) * 3)));
    RaiseTarget(state, target);
    auto& stream = state.ring;
    int offset = 0;
    while (offset < frames) {
        ma_uint32 count = static_cast<ma_uint32>(frames - offset);
        void* buffer = nullptr;
        ma_pcm_rb_acquire_write(&stream, &count, &buffer);
        if (count == 0) break; // Bounded latency: drop excess frames rather than block Unity's DSP.
        auto* destination = static_cast<float*>(buffer);
        for (ma_uint32 i = 0; i < count; ++i) {
            int sourceIndex = (offset + i) * channels;
            destination[i * 2] = data[sourceIndex] * leftGain;
            destination[i * 2 + 1] = data[sourceIndex + (channels > 1 ? 1 : 0)] * rightGain;
        }
        ma_pcm_rb_commit_write(&stream, count);
        offset += static_cast<int>(count);
    }
}

EXPORT void ra_close(int player) {
    if (player < 0 || player > 1) return;
    std::lock_guard<std::mutex> lock(outputs[player].producerMutex);
    Close(outputs[player]);
}

EXPORT void ra_shutdown() {
    ra_close(0);
    ra_close(1);
    if (initialized) ma_context_uninit(&context);
    initialized = false;
    devices.clear();
}
