#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#include <algorithm>
#include <mutex>
#include <vector>

#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

// Each Unity source is a single producer. Each device callback is a single consumer.
// Native callbacks allocate nothing and never take a lock.
constexpr int StreamCount = 3; // BGM + P1 engine + P2 engine, per output device.
struct Output {
    std::mutex producerMutex;
    ma_device device{};
    ma_pcm_rb streams[StreamCount]{};
    bool open = false;
};
static ma_context context;
static bool initialized = false;
static std::vector<ma_device_info> devices;
static Output outputs[2];

static void Mix(Output& output, float* data, ma_uint32 frames) {
    std::fill(data, data + frames * 2, 0.0f);
    for (auto& stream : output.streams) {
        ma_uint32 offset = 0;
        while (offset < frames) {
            ma_uint32 count = frames - offset;
            void* samples = nullptr;
            ma_pcm_rb_acquire_read(&stream, &count, &samples);
            if (count == 0) break; // An underrun contributes silence, never stale samples.
            auto* source = static_cast<float*>(samples);
            for (ma_uint32 i = 0; i < count * 2; ++i) data[offset * 2 + i] += source[i];
            ma_pcm_rb_commit_read(&stream, count);
            offset += count;
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
    for (auto& stream : output.streams) ma_pcm_rb_uninit(&stream);
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

EXPORT int ra_open(int player, int deviceIndex, int sampleRate) {
    if (player < 0 || player > 1 || !initialized || deviceIndex < 0 ||
        deviceIndex >= static_cast<int>(devices.size()) || sampleRate <= 0) return -1;
    auto& output = outputs[player];
    std::lock_guard<std::mutex> lock(output.producerMutex);
    Close(output);
    int ringCount = 0;
    for (auto& stream : output.streams) {
        if (ma_pcm_rb_init(ma_format_f32, 2, 8192, nullptr, nullptr, &stream) != MA_SUCCESS) {
            for (int i = 0; i < ringCount; ++i) ma_pcm_rb_uninit(&output.streams[i]);
            return -2;
        }
        ++ringCount;
    }
    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.playback.pDeviceID = &devices[deviceIndex].id;
    config.playback.format = ma_format_f32;
    config.playback.channels = 2;
    config.sampleRate = static_cast<ma_uint32>(sampleRate);
    config.dataCallback = Callback;
    config.pUserData = &output;
    ma_result result = ma_device_init(&context, &config, &output.device);
    if (result != MA_SUCCESS) {
        for (auto& stream : output.streams) ma_pcm_rb_uninit(&stream);
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
    auto& stream = output.streams[streamIndex];
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
