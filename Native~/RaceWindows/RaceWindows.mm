#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#include <atomic>
#include <mutex>
#include <memory>
#include "IUnityGraphics.h"
#include "IUnityGraphicsMetal.h"

// AppKit stays on the main thread. Texture ownership is shared with Unity's
// render thread; all rendering/presentation uses Unity's own command buffer.
static IUnityGraphicsMetalV2* metal;
static NSWindow* playerWindow;
static CAMetalLayer* outputLayer;
static id<MTLRenderPipelineState> pipeline;
static std::mutex outputMutex;
static std::atomic<int> pixelWidth{0}, pixelHeight{0};
static std::atomic<bool> visible{false}, closeRequested{false};

static void OnMain(dispatch_block_t action)
{
    if ([NSThread isMainThread]) action(); else dispatch_sync(dispatch_get_main_queue(), action);
}

@interface RacePlayerWindow : NSWindow <NSWindowDelegate>
@end
@implementation RacePlayerWindow
// Both keyboard input maps continue to go to Unity's primary window.
- (BOOL)canBecomeKeyWindow { return NO; }
- (BOOL)canBecomeMainWindow { return NO; }
- (BOOL)windowShouldClose:(NSWindow*)sender { closeRequested = true; return NO; }
- (void)windowDidResize:(NSNotification*)notification { [self updateOutputSize]; }
- (void)windowDidChangeBackingProperties:(NSNotification*)notification { [self updateOutputSize]; }
- (void)windowDidMiniaturize:(NSNotification*)notification { visible = false; }
- (void)windowDidDeminiaturize:(NSNotification*)notification { [self updateOutputSize]; }
- (void)updateOutputSize
{
    NSSize size = [self.contentView convertSizeToBacking:self.contentView.bounds.size];
    std::lock_guard<std::mutex> lock(outputMutex);
    outputLayer.contentsScale = self.backingScaleFactor;
    outputLayer.drawableSize = size;
    pixelWidth = (int)size.width; pixelHeight = (int)size.height;
    visible = self.visible && !self.miniaturized;
}
@end

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces)
{
    metal = interfaces->Get<IUnityGraphicsMetalV2>();
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() { metal = nullptr; }

extern "C" int UNITY_INTERFACE_EXPORT RaceWindows_Open(int screenIndex, int width, int height)
{
    if (!metal) return 0;
    __block int success = 0;
    OnMain(^{
        if (playerWindow) { success = 1; return; }
        NSWindow* primary = NSApp.mainWindow ?: NSApp.keyWindow;
        primary.title = @"RACING — P1";
        NSArray<NSScreen*>* screens = NSScreen.screens;
        if (screens.count == 0) return;
        NSScreen* screen = screenIndex >= 0 && screenIndex < (int)screens.count
            ? screens[screenIndex] : (primary.screen ?: screens[0]);
        NSRect area = screen.visibleFrame;
        CGFloat w = MIN(width, area.size.width * 0.85);
        CGFloat h = MIN(height, (area.size.height - 32) * 0.85);
        NSRect content = NSMakeRect(area.origin.x + (area.size.width - w) / 2,
            area.origin.y + (area.size.height - h) / 2, w, h);
        if (screen == primary.screen) { content.origin.x += 24; content.origin.y -= 24; }
        RacePlayerWindow* window = [[RacePlayerWindow alloc] initWithContentRect:content
            styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable |
                NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable
            backing:NSBackingStoreBuffered defer:NO screen:screen];
        window.title = @"RACING — P2";
        window.releasedWhenClosed = NO;
        window.delegate = window;
        window.contentMinSize = NSMakeSize(320, 180);
        window.collectionBehavior = NSWindowCollectionBehaviorFullScreenNone;
        CAMetalLayer* layer = [CAMetalLayer layer];
        layer.device = metal->MetalDevice();
        layer.pixelFormat = MTLPixelFormatBGRA8Unorm_sRGB;
        layer.framebufferOnly = getenv("RACING_WINDOW_CAPTURE") == nullptr;
        layer.displaySyncEnabled = YES;
        layer.allowsNextDrawableTimeout = YES;
        window.contentView.wantsLayer = YES;
        window.contentView.layer = layer;
        {
            std::lock_guard<std::mutex> lock(outputMutex);
            playerWindow = window; outputLayer = layer;
        }
        closeRequested = false;
        [window orderFront:nil];
        [window updateOutputSize];
        success = 1;
    });
    return success;
}

extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_Size(int* width, int* height)
{
    *width = pixelWidth; *height = pixelHeight;
}
extern "C" int UNITY_INTERFACE_EXPORT RaceWindows_CloseRequested() { return closeRequested ? 1 : 0; }
struct Frame
{
    __strong CAMetalLayer* layer;
    __strong id<MTLTexture> texture;
};
extern "C" void* UNITY_INTERFACE_EXPORT RaceWindows_Frame(void* texture)
{
    std::lock_guard<std::mutex> lock(outputMutex);
    return new Frame{outputLayer, (__bridge id<MTLTexture>)texture};
}
extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_Close()
{
    visible = false;
    OnMain(^{
        NSWindow* window;
        {
            std::lock_guard<std::mutex> lock(outputMutex);
            window = playerWindow; playerWindow = nil;
            outputLayer = nil;
        }
        window.delegate = nil;
        [window close];
    });
}

static void UNITY_INTERFACE_API Present(int eventId, void* data)
{
    @autoreleasepool {
        std::unique_ptr<Frame> frame(static_cast<Frame*>(data));
        if (!metal || !visible) return;
        CAMetalLayer* layer = frame->layer;
        id<MTLTexture> texture = frame->texture;
        if (!layer || !texture) return;
        if (!pipeline) {
            NSString* source = @"#include <metal_stdlib>\nusing namespace metal;\n"
                "struct V { float4 p [[position]]; float2 uv; };\n"
                "vertex V vert(uint i [[vertex_id]]) { float2 p=float2((i<<1)&2,i&2); "
                "return {float4(p*2-1,0,1),p}; }\n"
                "fragment float4 frag(V v [[stage_in]], texture2d<float> t [[texture(0)]]) { "
                "constexpr sampler s(filter::linear, address::clamp_to_edge); return t.sample(s,v.uv); }";
            NSError* error = nil;
            id<MTLLibrary> library = [layer.device newLibraryWithSource:source options:nil error:&error];
            MTLRenderPipelineDescriptor* descriptor = [MTLRenderPipelineDescriptor new];
            descriptor.vertexFunction = [library newFunctionWithName:@"vert"];
            descriptor.fragmentFunction = [library newFunctionWithName:@"frag"];
            descriptor.colorAttachments[0].pixelFormat = layer.pixelFormat;
            pipeline = [layer.device newRenderPipelineStateWithDescriptor:descriptor error:&error];
            if (!pipeline) { NSLog(@"RaceWindows: %@", error); return; }
        }
        id<CAMetalDrawable> drawable = [layer nextDrawable];
        if (!drawable) return;
        metal->EndCurrentCommandEncoder();
        id<MTLCommandBuffer> command = metal->CurrentCommandBuffer();
        if (!command) return;
        MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
        pass.colorAttachments[0].texture = drawable.texture;
        pass.colorAttachments[0].loadAction = MTLLoadActionDontCare;
        pass.colorAttachments[0].storeAction = MTLStoreActionStore;
        id<MTLRenderCommandEncoder> encoder = [command renderCommandEncoderWithDescriptor:pass];
        [encoder setRenderPipelineState:pipeline];
        [encoder setFragmentTexture:texture atIndex:0];
        [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:3];
        [encoder endEncoding];
        // Opt-in render artifact for validation; no screen capture or CPU copy
        // during normal play. Unity RenderTextures have a bottom-left origin.
        static int renderedFrames = 0;
        const char* capturePath = getenv("RACING_WINDOW_CAPTURE");
        if (++renderedFrames == 10 && capturePath) {
            NSString* path = [NSString stringWithUTF8String:capturePath];
            NSUInteger width = drawable.texture.width, height = drawable.texture.height;
            NSUInteger rowBytes = (width * 4 + 255) & ~255UL;
            id<MTLBuffer> readback = [layer.device newBufferWithLength:rowBytes * height options:MTLResourceStorageModeShared];
            id<MTLBlitCommandEncoder> copy = [command blitCommandEncoder];
            [copy copyFromTexture:drawable.texture sourceSlice:0 sourceLevel:0
                sourceOrigin:MTLOriginMake(0, 0, 0) sourceSize:MTLSizeMake(width, height, 1)
                toBuffer:readback destinationOffset:0 destinationBytesPerRow:rowBytes
                destinationBytesPerImage:rowBytes * height];
            [copy endEncoding];
            [command addCompletedHandler:^(id<MTLCommandBuffer> completed) {
                @autoreleasepool {
                    if (completed.status != MTLCommandBufferStatusCompleted) return;
                    NSBitmapImageRep* image = [[NSBitmapImageRep alloc] initWithBitmapDataPlanes:nil
                        pixelsWide:width pixelsHigh:height bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES
                        isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace
                        bitmapFormat:NSBitmapFormatAlphaNonpremultiplied bytesPerRow:width*4 bitsPerPixel:32];
                    const uint8_t* source = (const uint8_t*)readback.contents;
                    uint8_t* pixels = image.bitmapData;
                    for (NSUInteger y = 0; y < height; ++y)
                        for (NSUInteger x = 0; x < width; ++x) {
                            const uint8_t* from = source + y*rowBytes + x*4;
                            uint8_t* to = pixels + (y*width+x)*4;
                            to[0]=from[2]; to[1]=from[1]; to[2]=from[0]; to[3]=from[3];
                        }
                    [[image representationUsingType:NSBitmapImageFileTypePNG properties:@{}] writeToFile:path atomically:YES];
                }
            }];
        }
        [command presentDrawable:drawable];
    }
}
extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT RaceWindows_GetRenderEvent() { return Present; }
