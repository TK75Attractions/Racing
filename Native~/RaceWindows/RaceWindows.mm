#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ColorSync/ColorSync.h>
#include <atomic>
#include <mutex>
#include <memory>
#include <cstring>
#include "IUnityGraphics.h"
#include "IUnityGraphicsMetal.h"
#include "ExternalDisplayLayout.h"

// AppKit stays on the main thread. Texture ownership is shared with Unity's
// render thread; all rendering/presentation uses Unity's own command buffer.
static IUnityGraphicsMetalV2* metal;
static NSWindow* playerWindow;
static NSWindow* broadcastWindow;
static CAMetalLayer* broadcastLayer;
static std::atomic<int> broadcastWidth{0}, broadcastHeight{0};
static std::atomic<bool> broadcastVisible{false}, broadcastClosed{false};
static std::atomic<bool> broadcastBuiltIn{false}, broadcastFillsScreen{false};
static CAMetalLayer* outputLayer;
static id<MTLRenderPipelineState> pipeline;
static std::mutex outputMutex;
static std::atomic<int> pixelWidth{0}, pixelHeight{0};
static std::atomic<bool> visible{false}, closeRequested{false};
static std::atomic<bool> externalFullscreen{false};
static __weak NSWindow* unityWindow;
static id screenObserver;
static racing::ExternalDisplayAssignment externalAssignment;
static std::string broadcastDisplay;
struct WindowSuspension {
    bool disconnected=false, wasVisible=false;
};
static WindowSuspension primarySuspension, secondarySuspension, broadcastSuspension;

static void SuspendWindow(NSWindow* window, WindowSuspension& suspension)
{
    if (!window || suspension.disconnected) return;
    suspension.disconnected=true;
    // macOS may have hidden/moved the window before delivering the screen event.
    suspension.wasVisible=suspension.wasVisible || window.visible || window.miniaturized;
    [window orderOut:nil];
}
static void ResumeWindow(NSWindow* window, WindowSuspension& suspension)
{
    if (!window || !suspension.disconnected) return;
    bool show=suspension.wasVisible;
    suspension={};
    if (show) {
        if (window.miniaturized) [window deminiaturize:nil];
        [window orderFront:nil]; // Reconnection must not take focus from the surviving player.
    }
}
static std::string DisplayIdentity(CGDirectDisplayID display)
{
    CFUUIDRef uuid=CGDisplayCreateUUIDFromDisplayID(display);
    if (!uuid) return "id:"+std::to_string(display);
    NSString* identity=CFBridgingRelease(CFUUIDCreateString(kCFAllocatorDefault,uuid));
    CFRelease(uuid);
    return identity.UTF8String ?: "";
}
static std::vector<racing::DisplayArea> ConnectedDisplays()
{
    std::vector<racing::DisplayArea> displays;
    for (NSScreen* screen in NSScreen.screens) {
        CGDirectDisplayID id=[screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue];
        NSRect r=screen.frame;
        displays.push_back({id,(bool)CGDisplayIsBuiltin(id),CGDisplayMirrorsDisplay(id)!=kCGNullDirectDisplay,
            r.origin.x,r.origin.y,r.size.width,r.size.height,DisplayIdentity(id)});
    }
    return displays;
}
static NSRect DisplayFrame(const racing::DisplayArea& display)
{
    return NSMakeRect(display.x,display.y,display.width,display.height);
}
static void RefreshExternalLayout();
static void EnsureScreenObserver()
{
    if (!screenObserver) screenObserver=[NSNotificationCenter.defaultCenter
        addObserverForName:NSApplicationDidChangeScreenParametersNotification object:nil queue:NSOperationQueue.mainQueue
        usingBlock:^(NSNotification* notification) { RefreshExternalLayout(); }];
}
static void RemoveUnusedScreenObserver()
{
    if (!playerWindow && !broadcastWindow && screenObserver) {
        [NSNotificationCenter.defaultCenter removeObserver:screenObserver]; screenObserver=nil;
    }
}

static void OnMain(dispatch_block_t action)
{
    if ([NSThread isMainThread]) action(); else dispatch_sync(dispatch_get_main_queue(), action);
}

@interface RacePlayerWindow : NSWindow <NSWindowDelegate>
- (void)updateOutputSize;
@end

@interface RaceBroadcastWindow : RacePlayerWindow
@end

struct WindowPlacement {
    bool saved=false;
    NSRect frame;
    NSWindowStyleMask style;
    NSWindowCollectionBehavior behavior;
    NSInteger level;
    NSSize minimum, maximum, increments;
    BOOL shadow;
    NSWindowTitleVisibility title;
    BOOL transparent, closeHidden, minimizeHidden, zoomHidden;
};
static WindowPlacement primaryPlacement, secondaryPlacement;

static void RememberWindow(NSWindow* window, WindowPlacement& placement)
{
    if (!window || placement.saved) return;
    placement={true,window.frame,window.styleMask,window.collectionBehavior,window.level,
        window.minSize,window.maxSize,window.resizeIncrements,window.hasShadow,window.titleVisibility,window.titlebarAppearsTransparent,
        [window standardWindowButton:NSWindowCloseButton].hidden,[window standardWindowButton:NSWindowMiniaturizeButton].hidden,
        [window standardWindowButton:NSWindowZoomButton].hidden};
}
static bool FillScreen(NSWindow* window, NSRect frame)
{
    // A plain borderless NSWindow cannot receive keyboard focus. Give Unity a
    // full-content window with invisible chrome, preserving its original class/delegate.
    const auto style=window==unityWindow
        ? NSWindowStyleMaskTitled | NSWindowStyleMaskResizable | NSWindowStyleMaskFullSizeContentView
        : NSWindowStyleMaskBorderless | NSWindowStyleMaskResizable;
    const auto behavior=NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary;
    if (NSEqualRects(window.frame,frame) && window.styleMask==style && window.level==NSMainMenuWindowLevel+1 && window.collectionBehavior==behavior) return false;
    window.styleMask=style;
    window.titleVisibility=NSWindowTitleHidden;
    window.titlebarAppearsTransparent=YES;
    for (NSNumber* button in @[@(NSWindowCloseButton),@(NSWindowMiniaturizeButton),@(NSWindowZoomButton)])
        [window standardWindowButton:(NSWindowButton)button.integerValue].hidden=YES;
    window.collectionBehavior=behavior;
    window.level=NSMainMenuWindowLevel+1;
    window.hasShadow=NO;
    window.minSize=NSMakeSize(320,180);
    window.maxSize=NSMakeSize(10000,10000);
    window.resizeIncrements=NSMakeSize(1,1);
    [window setFrame:frame display:NO];
    return true;
}
static void RestoreWindow(NSWindow* window, WindowPlacement& placement)
{
    if (!window || !placement.saved) return;
    window.styleMask=placement.style;
    window.collectionBehavior=placement.behavior;
    window.level=placement.level;
    window.hasShadow=placement.shadow;
    window.minSize=placement.minimum;
    window.maxSize=placement.maximum;
    window.resizeIncrements=placement.increments;
    window.titleVisibility=placement.title;
    window.titlebarAppearsTransparent=placement.transparent;
    [window standardWindowButton:NSWindowCloseButton].hidden=placement.closeHidden;
    [window standardWindowButton:NSWindowMiniaturizeButton].hidden=placement.minimizeHidden;
    [window standardWindowButton:NSWindowZoomButton].hidden=placement.zoomHidden;
    NSRect frame=placement.frame;
    bool onscreen=false;
    for (NSScreen* screen in NSScreen.screens) if (NSIntersectsRect(frame,screen.visibleFrame)) onscreen=true;
    if (!onscreen && NSScreen.mainScreen) {
        NSRect area=NSScreen.mainScreen.visibleFrame;
        frame.size.width=MIN(frame.size.width,area.size.width);
        frame.size.height=MIN(frame.size.height,area.size.height);
        frame.origin=NSMakePoint(NSMidX(area)-frame.size.width/2,NSMidY(area)-frame.size.height/2);
    }
    [window setFrame:frame display:NO];
    placement.saved=false;
}
static NSWindow* FindUnityWindow()
{
    NSWindow* candidate=NSApp.mainWindow ?: NSApp.keyWindow;
    if (candidate && candidate!=playerWindow && candidate!=broadcastWindow && ![candidate isKindOfClass:NSPanel.class]) return candidate;
    // Background launches can create the Unity window before it becomes main/key.
    for (NSWindow* window in NSApp.windows)
        if (window!=playerWindow && window!=broadcastWindow && ![window isKindOfClass:NSPanel.class] && window.contentView &&
            ((window.styleMask&NSWindowStyleMaskTitled) || [NSStringFromClass(window.class) containsString:@"Unity"])) return window;
    return nil;
}
static void ApplyPlayerLayout(const std::vector<racing::DisplayArea>& displays, bool windowed)
{
    if (!playerWindow || !unityWindow) return;
    if (windowed) {
        if (externalFullscreen.exchange(false)) {
            RestoreWindow(unityWindow,primaryPlacement); RestoreWindow(playerWindow,secondaryPlacement);
            ResumeWindow(unityWindow,primarySuspension); ResumeWindow(playerWindow,secondarySuspension);
            [(RacePlayerWindow*)playerWindow updateOutputSize];
        }
        externalAssignment.Reset();
        return;
    }
    auto pair=externalAssignment.Resolve(displays);
    if (!externalAssignment.Assigned()) return; // Preserve desktop fallback until the initial pair exists.
    bool activating=!externalFullscreen.exchange(true);
    NSWindow* windows[2]={unityWindow,playerWindow};
    WindowPlacement* placements[2]={&primaryPlacement,&secondaryPlacement};
    WindowSuspension* suspensions[2]={&primarySuspension,&secondarySuspension};
    for (int i=0;i<2;++i) {
        if (pair[i]<0) {
            SuspendWindow(windows[i],*suspensions[i]);
            if (i==1) visible=false;
            continue;
        }
        RememberWindow(windows[i],*placements[i]);
        bool reconnecting=suspensions[i]->disconnected;
        bool changed=FillScreen(windows[i],DisplayFrame(displays[pair[i]]));
        ResumeWindow(windows[i],*suspensions[i]);
        suspensions[i]->wasVisible=windows[i].visible || windows[i].miniaturized;
        if (i==1 && (activating || changed || reconnecting)) [(RacePlayerWindow*)playerWindow updateOutputSize];
    }
    // Only the initial placement focuses Unity. Unplugging/reconnecting either seat does not.
    if (activating && NSApp.active && pair[0]>=0) [unityWindow makeKeyWindow];
}
static void ApplyBroadcastLayout(const std::vector<racing::DisplayArea>& displays)
{
    if (!broadcastWindow || broadcastClosed || broadcastDisplay.empty()) return;
    int target=racing::FindDisplay(displays,broadcastDisplay);
    if (target<0) {
        SuspendWindow(broadcastWindow,broadcastSuspension); broadcastVisible=false;
    } else if (broadcastSuspension.disconnected) {
        FillScreen(broadcastWindow,DisplayFrame(displays[target]));
        ResumeWindow(broadcastWindow,broadcastSuspension);
        [(RaceBroadcastWindow*)broadcastWindow updateOutputSize];
    }
    if (target>=0) broadcastSuspension.wasVisible=broadcastWindow.visible || broadcastWindow.miniaturized;
}
static void RefreshExternalLayout()
{
    auto displays=ConnectedDisplays();
    if (playerWindow) {
        if (!unityWindow) {
            unityWindow=FindUnityWindow(); primaryPlacement.saved=false;
        }
        const char* forceWindowed=getenv("RACING_WINDOWED");
        bool windowed=(forceWindowed && strcmp(forceWindowed,"1")==0) ||
            [NSProcessInfo.processInfo.arguments containsObject:@"-racing-windowed"];
        ApplyPlayerLayout(displays,windowed);
    }
    ApplyBroadcastLayout(displays);
}

extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_UpdateLayout() { OnMain(^{RefreshExternalLayout();}); }
extern "C" int UNITY_INTERFACE_EXPORT RaceWindows_ExternalFullscreen() { return externalFullscreen?1:0; }
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
    visible = !secondarySuspension.disconnected && self.visible && !self.miniaturized;
}
@end

@implementation RaceBroadcastWindow
- (NSRect)constrainFrameRect:(NSRect)frameRect toScreen:(NSScreen*)screen
{
    return frameRect; // A borderless movable broadcast must not be shortened to the work area.
}
- (BOOL)windowShouldClose:(NSWindow*)sender { broadcastClosed=true; [self orderOut:nil]; broadcastVisible=false; return NO; }
- (void)windowDidMiniaturize:(NSNotification*)notification { broadcastVisible=false; }
- (void)windowDidMove:(NSNotification*)notification
{
    if (broadcastSuspension.disconnected || racing::FindDisplay(ConnectedDisplays(),broadcastDisplay)<0) return;
    NSScreen* screen=self.screen;
    if (screen) broadcastDisplay=DisplayIdentity([screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue]);
}
- (void)updateOutputSize
{
    NSSize size=[self.contentView convertSizeToBacking:self.contentView.bounds.size];
    std::lock_guard<std::mutex> lock(outputMutex);
    broadcastLayer.contentsScale=self.backingScaleFactor;
    broadcastLayer.drawableSize=size;
    broadcastWidth=(int)size.width; broadcastHeight=(int)size.height;
    broadcastVisible=!broadcastSuspension.disconnected && !broadcastClosed && self.visible && !self.miniaturized;
    NSScreen* screen=self.screen;
    broadcastBuiltIn=screen && CGDisplayIsBuiltin([screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue]);
    broadcastFillsScreen=screen && NSEqualRects(self.frame,screen.frame);
}
@end

static void PlaceBroadcastWindow(NSWindow* window, NSScreen* screen)
{
    // Use the same full-content, borderless window placement as P1/P2, without a macOS fullscreen Space.
    FillScreen(window,screen.frame);
    window.title=@"RACING — BROADCAST";
    window.movableByWindowBackground=YES;
    window.level=NSMainMenuWindowLevel+2;
    // The operator can still switch to another app on this PC.
    window.hidesOnDeactivate=YES;
    window.contentMinSize=NSMakeSize(480,270);
    // Apply only when opening: later user movement/resizing remains untouched.
}
extern "C" int UNITY_INTERFACE_EXPORT RaceWindows_OpenBroadcast()
{
    if (!metal) return 0;
    __block int success=0;
    OnMain(^{
        if (broadcastWindow) {
            broadcastClosed=false;
            if (broadcastSuspension.disconnected) broadcastSuspension.wasVisible=true;
            else [broadcastWindow orderFront:nil];
            RefreshExternalLayout(); [(RaceBroadcastWindow*)broadcastWindow updateOutputSize]; success=1; return;
        }
        NSArray<NSScreen*>* screens=NSScreen.screens;
        auto displays=ConnectedDisplays();
        int targetIndex=racing::BroadcastTarget(displays,CGMainDisplayID());
        if(targetIndex<0)return;
        NSScreen* target=screens[targetIndex];
        RaceBroadcastWindow* window=[[RaceBroadcastWindow alloc] initWithContentRect:target.visibleFrame
            styleMask:NSWindowStyleMaskBorderless|NSWindowStyleMaskResizable
            backing:NSBackingStoreBuffered defer:NO screen:target];
        window.releasedWhenClosed=NO; window.delegate=window;
        PlaceBroadcastWindow(window,target);
        CAMetalLayer* layer=[CAMetalLayer layer];
        layer.device=metal->MetalDevice(); layer.pixelFormat=MTLPixelFormatBGRA8Unorm_sRGB;
        layer.framebufferOnly=getenv("RACING_BROADCAST_CAPTURE")==nullptr; layer.displaySyncEnabled=YES; layer.allowsNextDrawableTimeout=YES;
        window.contentView.wantsLayer=YES; window.contentView.layer=layer;
        { std::lock_guard<std::mutex> lock(outputMutex); broadcastWindow=window; broadcastLayer=layer; }
        broadcastClosed=false; broadcastSuspension={};
        broadcastDisplay=DisplayIdentity([target.deviceDescription[@"NSScreenNumber"] unsignedIntValue]);
        [window orderFront:nil]; PlaceBroadcastWindow(window,target); [window updateOutputSize];
        EnsureScreenObserver(); success=1;
    });
    return success;
}
extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_BroadcastSize(int* width,int* height)
{
    // App activation can reveal a hidden-on-deactivate window without a resize notification.
    OnMain(^{if(broadcastWindow)[(RaceBroadcastWindow*)broadcastWindow updateOutputSize];});
    *width=broadcastWidth; *height=broadcastHeight;
}
extern "C" int UNITY_INTERFACE_EXPORT RaceWindows_BroadcastClosed() { return broadcastClosed?1:0; }
extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_BroadcastPlacement(int* builtIn,int* fillsScreen)
{ *builtIn=broadcastBuiltIn?1:0; *fillsScreen=broadcastFillsScreen?1:0; }
extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_CloseBroadcast()
{
    broadcastVisible=false;
    OnMain(^{
        NSWindow* window;
        { std::lock_guard<std::mutex> lock(outputMutex); window=broadcastWindow; broadcastWindow=nil; broadcastLayer=nil; }
        window.delegate=nil; [window close]; broadcastWidth=0; broadcastHeight=0;
        broadcastDisplay.clear(); broadcastSuspension={}; RemoveUnusedScreenObserver();
    });
}

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
        NSWindow* primary = FindUnityWindow();
        unityWindow=primary;
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
        externalAssignment.Reset(); primarySuspension={}; secondarySuspension={};
        [window orderFront:nil];
        RefreshExternalLayout();
        EnsureScreenObserver();
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
    bool visible;
};
extern "C" void* UNITY_INTERFACE_EXPORT RaceWindows_Frame(void* texture)
{
    std::lock_guard<std::mutex> lock(outputMutex);
    return new Frame{outputLayer, (__bridge id<MTLTexture>)texture,visible};
}
extern "C" void* UNITY_INTERFACE_EXPORT RaceWindows_BroadcastFrame(void* texture)
{
    std::lock_guard<std::mutex> lock(outputMutex);
    return new Frame{broadcastLayer,(__bridge id<MTLTexture>)texture,broadcastVisible};
}
extern "C" void UNITY_INTERFACE_EXPORT RaceWindows_Close()
{
    visible = false;
    OnMain(^{
        RestoreWindow(unityWindow,primaryPlacement);
        ResumeWindow(unityWindow,primarySuspension);
        externalAssignment.Reset(); secondarySuspension={};
        primaryPlacement.saved=false;
        externalFullscreen=false; secondaryPlacement.saved=false;
        unityWindow=nil;
        NSWindow* window;
        {
            std::lock_guard<std::mutex> lock(outputMutex);
            window = playerWindow; playerWindow = nil;
            outputLayer = nil;
        }
        window.delegate = nil;
        [window close];
        RemoveUnusedScreenObserver();
    });
}

static void UNITY_INTERFACE_API Present(int eventId, void* data)
{
    @autoreleasepool {
        std::unique_ptr<Frame> frame(static_cast<Frame*>(data));
        if (!metal || !frame || !frame->visible) return;
        // A queued frame can predate the unplug. Never wait for a missing screen's
        // drawable on Unity's shared render thread while the other outputs continue.
        if (!(eventId==1 ? broadcastVisible.load() : visible.load())) return;
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
        static int renderedFrames[2] = {0,0};
        int slot=eventId==1?1:0;
        const char* capturePath = getenv(slot==1?"RACING_BROADCAST_CAPTURE":"RACING_WINDOW_CAPTURE");
        if (++renderedFrames[slot] == 10 && capturePath) {
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
