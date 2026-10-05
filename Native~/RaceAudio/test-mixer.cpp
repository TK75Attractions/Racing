// Hardware-free validation of the same mixer and queues used by the shipped plugin.
#include "RaceAudio.cpp"
#include <cassert>
#include <cmath>
#include <iostream>
#include <thread>

static void Near(float actual, float expected) { assert(std::fabs(actual - expected) < 0.00001f); }
int main() {
    for (auto& output : outputs) {
        for (auto& stream : output.streams)
            assert(ma_pcm_rb_init(ma_format_f32, 2, 8192, nullptr, nullptr, &stream) == MA_SUCCESS);
        output.open = true;
    }
    float musicP1[] = { .25f, .5f, -.25f, -.5f };
    float musicP2[] = { .1f, .2f, .3f, .4f };
    float engine[] = { .2f, .2f, .4f, .4f };
    ra_push(0, 0, musicP1, 2, 2, 1, 1);
    ra_push(1, 0, musicP2, 2, 2, 1, 1);
    ra_push(0, 1, engine, 2, 2, .5f, .25f);
    ra_push(1, 1, engine, 2, 2, .25f, .5f);
    float p1[8], p2[8];
    Mix(outputs[0], p1, 4);
    Mix(outputs[1], p2, 4);
    Near(p1[0], .35f); Near(p1[1], .55f);
    Near(p1[2], -.05f); Near(p1[3], -.4f);
    Near(p2[0], .15f); Near(p2[1], .3f);
    Near(p2[2], .4f); Near(p2[3], .6f);
    for (int i = 4; i < 8; i++) { Near(p1[i], 0); Near(p2[i], 0); }
    Mix(outputs[0], p1, 4); // Drained rings never repeat audio.
    for (float sample : p1) Near(sample, 0);
    float mono[] = { .75f, -.75f };
    ra_push(0, 0, mono, 2, 1, 2, 2);
    Mix(outputs[0], p1, 2);
    Near(p1[0], 1); Near(p1[1], 1); Near(p1[2], -1); Near(p1[3], -1);
    // Full queues remain bounded; the next write does not block or overwrite unread PCM.
    std::vector<float> full(20000, .1f);
    ra_push(0, 0, full.data(), 10000, 2, 1, 1);
    assert(ma_pcm_rb_available_read(&outputs[0].streams[0]) == 8192);
    ra_push(0, 0, full.data(), 10000, 2, 1, 1);
    assert(ma_pcm_rb_available_read(&outputs[0].streams[0]) == 8192);
    ma_pcm_rb_reset(&outputs[0].streams[0]);
    // Exercise SPSC wraparound under concurrent production/consumption.
    std::thread producer([&] {
        for (int i = 0; i < 10000; i++) ra_push(0, 0, full.data(), 32, 2, 1, 1);
    });
    float buffer[64];
    for (int i = 0; i < 10000; i++) {
        Mix(outputs[0], buffer, 32);
        for (float sample : buffer) assert(sample == 0 || std::fabs(sample - .1f) < .00001f);
    }
    producer.join();
    for (auto& output : outputs) {
        output.open = false;
        for (auto& stream : output.streams) ma_pcm_rb_uninit(&stream);
    }
    ra_push(-1, 0, mono, 2, 1, 1, 1);
    ra_push(0, 10, mono, 2, 1, 1, 1);
    ra_shutdown();
    std::cout << "PASS: device isolation, independent stereo perspective, mixing, silence, mono, clipping, bounded queue, concurrent wraparound\n";
}
