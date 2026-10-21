// Hardware-free tests of the exact queues/resampler shipped in the plugin.
#include "RaceAudio.cpp"
#include <cassert>
#include <cmath>
#include <iostream>
#include <thread>

static void Near(float actual, float expected, float tolerance = .00002f) {
    assert(std::fabs(actual - expected) < tolerance);
}
static void Init(ma_uint32 minimumBufferMilliseconds = 50, ma_uint32 sampleRate = 48000) {
    for (auto& output : outputs) {
        output.sampleRate = sampleRate;
        output.minimumBufferMilliseconds.store(minimumBufferMilliseconds);
        for (auto& stream : output.streams) assert(InitStream(stream, output.sampleRate, minimumBufferMilliseconds));
        output.open = true;
    }
}
static void Clear() {
    for (auto& output : outputs) {
        output.open = false;
        for (auto& stream : output.streams) UninitStream(stream);
    }
}
static void Push(int player, int stream, float left, float right, float leftGain = 1, float rightGain = 1) {
    std::vector<float> data(1024 * 2);
    for (int i = 0; i < 1024; ++i) { data[i * 2] = left; data[i * 2 + 1] = right; }
    for (int i = 0; i < 4; ++i) ra_push(player, stream, data.data(), 1024, 2, leftGain, rightGain);
}
static void TestRouting() {
    Init();
    Push(0, 0, .25f, .5f); Push(1, 0, .1f, .2f);
    Push(0, 1, .2f, .2f, .5f, .25f); Push(1, 1, .2f, .2f, .25f, .5f);
    Push(0, 3, .2f, .2f, .5f, .25f); Push(0, 4, .2f, .2f, .25f, .5f);
    float p1[1024], p2[1024];
    Mix(outputs[0], p1, 512); Mix(outputs[1], p2, 512);
    Near(p1[600], .5f); Near(p1[601], .7f);
    Near(p2[600], .15f); Near(p2[601], .3f);
    for (int i = 0; i < 20; ++i) Mix(outputs[0], p1, 512);
    for (float sample : p1) Near(sample, 0); // No stale audio after starvation.
    assert(outputs[0].streams[0].underruns == 1);
    Clear();
    Init();
    std::vector<float> mono(1024, .75f);
    for (int i = 0; i < 4; ++i) ra_push(0, 0, mono.data(), 1024, 1, 2, 2);
    Mix(outputs[0], p1, 512);
    Near(p1[600], 1); Near(p1[601], 1);
    Clear();
}
static void TestPrimingAndRecovery() {
    Init();
    std::vector<float> data(2048, .2f);
    float buffer[512];
    ra_push(0, 0, data.data(), 1024, 2, 1, 1);
    Mix(outputs[0], buffer, 256);
    for (float sample : buffer) Near(sample, 0); // Wait for three DSP blocks.
    assert(!outputs[0].streams[0].primed);
    for (int i = 0; i < 2; ++i) ra_push(0, 0, data.data(), 1024, 2, 1, 1);
    Mix(outputs[0], buffer, 256);
    Near(buffer[400], .2f);
    for (int i = 0; i < 20; ++i) Mix(outputs[0], buffer, 256);
    assert(outputs[0].streams[0].underruns == 1);
    int refillBlocks = (outputs[0].streams[0].targetFrames.load() + 1023) / 1024;
    for (int i = 0; i < refillBlocks; ++i) {
        ra_push(0, 0, data.data(), 1024, 2, 1, 1);
        if (i + 1 < refillBlocks) {
            Mix(outputs[0], buffer, 256);
            for (float sample : buffer) Near(sample, 0);
        }
    }
    Mix(outputs[0], buffer, 256);
    Near(buffer[400], .2f);
    assert(outputs[0].streams[0].underruns == 1);
    Clear();
}
static void TestClockDriftAndJitter() {
    Init();
    constexpr double duration = 600.0;
    double producerTime = 0, deviceTime[2] = {0, 0};
    const int callbackFrames[2] = {480, 960};
    const double deviceRate[2] = {48000 * 1.0015, 48000 * .9985};
    const double jitter[] = {0, .006, -.003, .002, -.004, .001};
    int block = 0;
    std::vector<float> source(2048);
    float result[1920];
    bool started[2] = {false, false};
    ma_uint32 minQueue[2] = {RingFrames, RingFrames}, maxQueue[2] = {0, 0};
    float previous[2] = {0, 0};
    while (std::min(producerTime, std::min(deviceTime[0], deviceTime[1])) < duration) {
        if (producerTime <= deviceTime[0] && producerTime <= deviceTime[1]) {
            for (int i = 0; i < 1024; ++i) {
                float value = .2f + .1f * std::sin(2 * 3.141592653589793 * 220 * (block * 1024LL + i) / 48000);
                source[i * 2] = value; source[i * 2 + 1] = value * .5f;
            }
            for (int player = 0; player < 2; ++player)
                ra_push(player, 0, source.data(), 1024, 2, 1, 1);
            ++block;
            producerTime = block * 1024.0 / 48000 + jitter[block % 6];
        } else {
            int player = deviceTime[0] <= deviceTime[1] ? 0 : 1;
            Mix(outputs[player], result, callbackFrames[player]);
            auto& stream = outputs[player].streams[0];
            if (started[player]) {
                assert(stream.primed && stream.underruns == 0);
                for (int i = 0; i < callbackFrames[player]; ++i) {
                    assert(result[i * 2] > .09f && result[i * 2] < .31f);
                    assert(std::fabs(result[i * 2] - previous[player]) < .004f);
                    Near(result[i * 2 + 1], result[i * 2] * .5f);
                    previous[player] = result[i * 2];
                }
                auto queued = ma_pcm_rb_available_read(&stream.ring);
                minQueue[player] = std::min(minQueue[player], queued);
                maxQueue[player] = std::max(maxQueue[player], queued);
                assert(queued < RingFrames / 2);
            } else if (stream.primed) {
                started[player] = true; previous[player] = result[(callbackFrames[player] - 1) * 2];
            }
            deviceTime[player] += callbackFrames[player] / deviceRate[player];
        }
    }
    for (int player = 0; player < 2; ++player) {
        assert(started[player]);
        std::cout << "600s clock " << (player == 0 ? "+1500" : "-1500")
                  << "ppm: zero underruns, queue " << minQueue[player] << ".." << maxQueue[player] << " frames\n";
    }
    Clear();
}
static void TestBoundedConcurrentQueues() {
    Init();
    std::vector<float> full(RingFrames * 4, .1f);
    ra_push(0, 0, full.data(), RingFrames * 2, 2, 1, 1);
    assert(ma_pcm_rb_available_read(&outputs[0].streams[0].ring) == RingFrames);
    ra_push(0, 0, full.data(), RingFrames * 2, 2, 1, 1);
    assert(ma_pcm_rb_available_read(&outputs[0].streams[0].ring) == RingFrames);
    std::thread producer([&] {
        for (int i = 0; i < 10000; ++i) ra_push(0, 0, full.data(), 32, 2, 1, 1);
    });
    float buffer[64];
    for (int i = 0; i < 10000; ++i) {
        Mix(outputs[0], buffer, 32);
        for (float sample : buffer) assert(std::isfinite(sample) && sample >= 0 && sample <= .10001f);
    }
    producer.join();
    Clear();
    ra_push(-1, 0, full.data(), 2, 2, 1, 1);
    ra_push(0, StreamCount, full.data(), 2, 2, 1, 1);
}

static void TestAdaptiveRecovery() {
    Init();
    Push(0, 0, .2f, .1f);
    float result[960];
    for (int i = 0; i < 20; ++i) Mix(outputs[0], result, 480);
    auto& stream = outputs[0].streams[0];
    assert(stream.underruns == 1);
    ma_uint32 learned = stream.targetFrames.load();
    assert(learned > 3072 && learned <= outputs[0].sampleRate / 5);
    std::vector<float> source(2048, .2f);
    ra_push(0, 0, source.data(), 1024, 2, 1, 1);
    assert(stream.targetFrames.load() == learned); // The old producer overwrote recovery protection.
    assert(ra_get_underruns(0) == 1);
    assert(ra_get_underruns(-1) == 0);
    Clear();
}

static void TestBufferConfiguration() {
    Init();
    ra_set_min_buffer_milliseconds(0, 120);
    Push(0, 0, .2f, .1f);
    assert(outputs[0].streams[0].targetFrames.load() == 5760);
    ra_set_min_buffer_milliseconds(0, 999);
    assert(outputs[0].minimumBufferMilliseconds.load() == 150);
    ra_set_min_buffer_milliseconds(0, -1);
    assert(outputs[0].minimumBufferMilliseconds.load() == 50);
    ra_set_min_buffer_milliseconds(-1, 120);
    ra_set_min_buffer_milliseconds(2, 120);
    Clear();
}

static void TestEditorScheduling() {
    for (ma_uint32 rate : {44100u, 48000u, 96000u}) {
        Init(120, rate);
        const double duration = 60.0;
        double producerTime = 0, deviceTime[2] = {0, 0};
        const ma_uint32 callbackFrames[2] = {480, 960};
        const double deviceRate[2] = {rate * 1.0015, rate * .9985};
        const double jitter[] = {0, .006, -.003, .002, -.004, .001};
        int block = 0, crossings[2] = {0, 0};
        bool started[2] = {false, false};
        double startedAt[2] = {0, 0};
        float previous[2] = {0, 0};
        std::vector<float> source(2048);
        float result[1920];
        while (std::min(producerTime, std::min(deviceTime[0], deviceTime[1])) < duration) {
            if (producerTime <= deviceTime[0] && producerTime <= deviceTime[1]) {
                for (int i = 0; i < 1024; ++i) {
                    float value = .2f * std::sin(2 * 3.141592653589793 * 220 * (block * 1024LL + i) / rate);
                    source[i * 2] = value; source[i * 2 + 1] = value * .5f;
                }
                for (int player = 0; player < 2; ++player) ra_push(player, 0, source.data(), 1024, 2, 1, 1);
                ++block;
                // Delayed DSP delivery, followed by a catch-up burst, never time travel.
                producerTime = std::max(producerTime, block * 1024.0 / rate + jitter[block % 6]
                    + (block % 240 == 120 ? .080 : 0));
            } else {
                int player = deviceTime[0] <= deviceTime[1] ? 0 : 1;
                Mix(outputs[player], result, callbackFrames[player]);
                auto& stream = outputs[player].streams[0];
                assert(stream.underruns == 0);
                if (started[player]) {
                    assert(stream.primed);
                    for (ma_uint32 i = 0; i < callbackFrames[player]; ++i) {
                        float sample = result[i * 2];
                        assert(std::fabs(sample - previous[player]) < .008f);
                        Near(result[i * 2 + 1], sample * .5f);
                        if (previous[player] < 0 && sample >= 0) ++crossings[player];
                        previous[player] = sample;
                    }
                } else if (stream.primed) {
                    started[player] = true;
                    startedAt[player] = deviceTime[player] + callbackFrames[player] / deviceRate[player];
                    previous[player] = result[(callbackFrames[player] - 1) * 2];
                }
                deviceTime[player] += callbackFrames[player] / deviceRate[player];
            }
        }
        for (int player = 0; player < 2; ++player) {
            assert(started[player] && ra_get_underruns(player) == 0);
            double frequency = crossings[player] / (deviceTime[player] - startedAt[player]);
            assert(std::fabs(frequency - 220) < .25); // No accumulating music slowdown.
            std::cout << "60s Editor " << rate << "Hz P" << player + 1
                      << ": repeated 80ms stalls, zero underruns, tone=" << frequency << "Hz\n";
        }
        Clear();
    }
}
int main() {
    TestRouting(); TestPrimingAndRecovery(); TestClockDriftAndJitter(); TestBoundedConcurrentQueues();
    TestAdaptiveRecovery(); TestBufferConfiguration(); TestEditorScheduling();
    ra_shutdown();
    std::cout << "PASS: stereo/device/CPU isolation, mono, clipping, priming, starvation/recovery, clock drift, DSP jitter, bounded concurrent queues\n";
}
