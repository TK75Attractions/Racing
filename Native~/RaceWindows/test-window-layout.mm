// The production helpers are compiled into a hidden-window AppKit host; no Unity or physical screen emulation.
#include "RaceWindows.mm"
#include <cassert>
#include <iostream>

// Observe ordering without ever showing a test window on the operator's desktop.
@interface LayoutProbePlayer : RacePlayerWindow
@property BOOL requestedVisible;
@property int shown, hidden, resized, focused;
- (BOOL)actuallyVisible;
@end
@implementation LayoutProbePlayer
- (NSRect)constrainFrameRect:(NSRect)frame toScreen:(NSScreen*)screen { return frame; }
- (BOOL)isVisible { return self.requestedVisible; }
- (BOOL)actuallyVisible { return [super isVisible]; }
- (void)orderFront:(id)sender { self.shown++; self.requestedVisible=YES; }
- (void)orderOut:(id)sender { self.hidden++; self.requestedVisible=NO; }
- (void)makeKeyWindow { self.focused++; }
- (void)setFrame:(NSRect)frame display:(BOOL)display { self.resized++; [super setFrame:frame display:display]; }
@end
@interface LayoutProbeBroadcast : RaceBroadcastWindow
@property BOOL requestedVisible;
@property int shown, hidden;
- (BOOL)actuallyVisible;
@end
@implementation LayoutProbeBroadcast
- (BOOL)isVisible { return self.requestedVisible; }
- (BOOL)actuallyVisible { return [super isVisible]; }
- (void)orderFront:(id)sender { self.shown++; self.requestedVisible=YES; }
- (void)orderOut:(id)sender { self.hidden++; self.requestedVisible=NO; }
@end

static void TestIndependentReconnect(NSRect initial)
{
    LayoutProbePlayer* primary=[[LayoutProbePlayer alloc] initWithContentRect:initial
        styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];
    LayoutProbePlayer* secondary=[[LayoutProbePlayer alloc] initWithContentRect:initial
        styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];
    LayoutProbeBroadcast* broadcast=[[LayoutProbeBroadcast alloc] initWithContentRect:initial
        styleMask:NSWindowStyleMaskBorderless|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];
    for (NSWindow* window in @[primary,secondary,broadcast]) window.releasedWhenClosed=NO;
    unityWindow=primary; playerWindow=secondary; broadcastWindow=broadcast;
    primary.requestedVisible=secondary.requestedVisible=broadcast.requestedVisible=YES;
    externalAssignment.Reset(); externalFullscreen=false;
    primaryPlacement={}; secondaryPlacement={}; primarySuspension={}; secondarySuspension={}; broadcastSuspension={};
    broadcastClosed=closeRequested=false;
    racing::DisplayArea p1={20,false,false,-1920,0,1920,1080,"p1"};
    racing::DisplayArea p2={30,false,false,1440,0,2560,1440,"p2"};
    racing::DisplayArea builtIn={10,true,false,0,0,1440,900,"operator"};
    racing::DisplayArea unrelated={40,false,false,4000,0,1920,1080,"unrelated"};
    broadcastDisplay=broadcastDefaultDisplay="operator";
    FillScreen(broadcast,DisplayFrame(builtIn));
    ApplyPlayerLayout({builtIn,p2,p1},false);
    assert(externalFullscreen && NSEqualRects(primary.frame,DisplayFrame(p1)) && NSEqualRects(secondary.frame,DisplayFrame(p2)));
    int primaryResizes=primary.resized, primaryFocus=primary.focused;
    int secondaryResizes=secondary.resized;
    // The disconnected P2 may already have been moved by macOS; its physical identity still wins.
    [secondary setFrame:primary.frame display:NO];
    secondary.requestedVisible=NO; // The OS can also hide it before the topology event.
    ApplyPlayerLayout({builtIn,p1},false);
    assert(secondarySuspension.disconnected && !secondary.requestedVisible && primary.requestedVisible);
    assert(primary.resized==primaryResizes && primary.focused==primaryFocus);
    int hidden=secondary.hidden;
    ApplyPlayerLayout({unrelated,p1,builtIn},false);
    [secondary updateOutputSize];
    assert(secondary.hidden==hidden && !visible && !closeRequested);
    assert(primary.resized==primaryResizes && NSEqualRects(primary.frame,DisplayFrame(p1)));
    // Reconnection changes numeric ID, backing size and arrangement; P1 is untouched.
    p2.id=300; p2.x=-5760; p2.width=3840; p2.height=2160;
    ApplyPlayerLayout({p2,builtIn,p1},false);
    assert(!secondarySuspension.disconnected && secondary.requestedVisible && secondary.shown==1);
    assert(NSEqualRects(secondary.frame,DisplayFrame(p2)) && secondary.resized>secondaryResizes);
    assert(primary.resized==primaryResizes && primary.focused==primaryFocus);
    secondaryResizes=secondary.resized;
    ApplyPlayerLayout({builtIn,p2,unrelated},false);
    assert(primarySuspension.disconnected && !primary.requestedVisible && secondary.requestedVisible);
    assert(secondary.resized==secondaryResizes && NSEqualRects(secondary.frame,DisplayFrame(p2)));
    ApplyPlayerLayout({},false);
    assert(primarySuspension.disconnected && secondarySuspension.disconnected);
    ApplyPlayerLayout({p2},false);
    assert(!primary.requestedVisible && secondary.requestedVisible && secondary.shown==2);
    p1.id=200; p1.x=1440; p1.width=1080; p1.height=1920;
    ApplyPlayerLayout({builtIn,p1,p2},false);
    assert(primary.requestedVisible && primary.shown==1 && primary.focused==primaryFocus);
    assert(NSEqualRects(primary.frame,DisplayFrame(p1)) && secondary.resized==secondaryResizes);
    // Losing the operator display affects neither player nor the broadcast's close semantics.
    primaryResizes=primary.resized;
    ApplyBroadcastLayout({p1,p2});
    assert(broadcastSuspension.disconnected && !broadcast.requestedVisible && !broadcastVisible && !broadcastClosed);
    assert(primary.resized==primaryResizes && secondary.resized==secondaryResizes);
    builtIn.id=100; builtIn.width=1728; builtIn.height=1117;
    ApplyBroadcastLayout({p2,builtIn,p1});
    assert(broadcast.requestedVisible && broadcast.shown==1 && !broadcastSuspension.disconnected);
    assert(NSEqualRects(broadcast.frame,DisplayFrame(builtIn)));
    NSRect moved=NSMakeRect(25,35,800,450);
    [broadcast setFrame:moved display:NO];
    ApplyBroadcastLayout({builtIn,p1});
    assert(NSEqualRects(broadcast.frame,moved)); // Healthy manual placement is preserved.
    ApplyBroadcastLayout({p1,p2});
    broadcastClosed=true;
    ApplyBroadcastLayout({builtIn,p1,p2});
    assert(!broadcast.requestedVisible && broadcast.shown==1); // A user-closed window never reopens.
    // Explicit restore fills every assigned display, including manually hidden or resized windows.
    broadcastClosed=false; broadcastSuspension={}; broadcast.requestedVisible=YES;
    broadcastDisplay="p1"; // The operator previously moved the broadcast to a player display.
    [primary setFrame:moved display:NO]; [secondary setFrame:moved display:NO];
    [broadcast setFrame:moved display:NO];
    primary.requestedVisible=secondary.requestedVisible=NO;
    assert(RepositionWindows({builtIn,p1,p2}));
    assert(operatorFullscreen && primary.requestedVisible && secondary.requestedVisible && broadcast.requestedVisible);
    assert(NSEqualRects(primary.frame,DisplayFrame(p1)) && NSEqualRects(secondary.frame,DisplayFrame(p2)));
    assert(NSEqualRects(broadcast.frame,DisplayFrame(builtIn)) && broadcastDisplay=="operator");
    assert(!closeRequested && !broadcastClosed && primary.focused==primaryFocus);
    // A missing assigned monitor remains hidden, even with an unrelated monitor connected.
    assert(RepositionWindows({builtIn,p1,unrelated}));
    assert(secondarySuspension.disconnected && !secondary.requestedVisible && primary.requestedVisible);
    ApplyPlayerLayout({builtIn,p1,p2},false);
    assert(secondary.requestedVisible && NSEqualRects(secondary.frame,DisplayFrame(p2)));
    // The operator display also keeps its default identity while unplugged.
    assert(RepositionWindows({p1,p2}));
    assert(broadcastSuspension.disconnected && !broadcast.requestedVisible);
    ApplyBroadcastLayout({builtIn,p1,p2});
    assert(broadcast.requestedVisible && NSEqualRects(broadcast.frame,DisplayFrame(builtIn)));
    // An explicit windowed override still exits automatic layout and restores desktop styles.
    ApplyPlayerLayout({builtIn,p2},true);
    assert(!externalFullscreen && !externalAssignment.Assigned());
    operatorFullscreen=false;
    assert(!RepositionWindows({builtIn,p1})); // No initial two-screen assignment: leave the desktop alone.
    assert(primary.styleMask & NSWindowStyleMaskTitled);
    assert(secondary.styleMask & NSWindowStyleMaskTitled);
    assert(!primary.actuallyVisible && !secondary.actuallyVisible && !broadcast.actuallyVisible);
    for (NSWindow* window in @[primary,secondary,broadcast]) { window.delegate=nil; [window close]; }
    unityWindow=nil; playerWindow=broadcastWindow=nil;
    primaryPlacement={}; secondaryPlacement={}; primarySuspension={}; secondarySuspension={}; broadcastSuspension={};
    broadcastDisplay.clear(); broadcastClosed=false;
    std::cout<<"DISPLAY_RECONNECT_WINDOWS_PASS: production AppKit layout; independent P1/P2/broadcast hide/restore, OS rehome, new IDs/sizes, repeated/both loss, no surviving-window resize/focus, preserved manual/closed windows. No test windows shown.\n";
}

int main()
{
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSRect initial=NSMakeRect(100,100,640,360);
        if (NSScreen.mainScreen) {
            NSRect area=NSScreen.mainScreen.visibleFrame;
            initial.origin=NSMakePoint(NSMidX(area)-320,NSMidY(area)-180);
        }
        NSWindow* primary=[[NSWindow alloc] initWithContentRect:initial
            styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];
        primary.releasedWhenClosed=NO;
        unityWindow=primary;
        WindowPlacement placement;
        NSRect original=primary.frame;
        RememberWindow(primary,placement);
        for (NSValue* value in @[[NSValue valueWithRect:NSMakeRect(-1920,0,1920,1080)],
            [NSValue valueWithRect:NSMakeRect(1440,-200,1440,2560)],
            [NSValue valueWithRect:NSMakeRect(0,0,2880,1800)]]) {
            NSRect frame=value.rectValue;
            assert(FillScreen(primary,frame));
            assert(NSEqualRects(primary.frame,frame));
            assert(NSEqualSizes(primary.contentView.bounds.size,frame.size));
            assert(primary.canBecomeKeyWindow);
            assert(primary.titleVisibility==NSWindowTitleHidden && primary.titlebarAppearsTransparent);
            assert([primary standardWindowButton:NSWindowZoomButton].hidden);
            assert(primary.level>NSMainMenuWindowLevel);
            assert(!FillScreen(primary,frame)); // polling must not continually resize or steal focus
            RememberWindow(primary,placement); // a repeated layout does not overwrite desktop restore geometry
            assert(NSEqualRects(placement.frame,original));
        }
        RestoreWindow(primary,placement);
        assert(!placement.saved);
        assert(primary.styleMask & NSWindowStyleMaskTitled);
        assert(primary.level==NSNormalWindowLevel);
        assert(NSEqualRects(primary.frame,original));
        assert(!primary.visible);
        RacePlayerWindow* secondary=[[RacePlayerWindow alloc] initWithContentRect:initial
            styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];
        secondary.releasedWhenClosed=NO;
        playerWindow=secondary;
        assert(FindUnityWindow()==primary); // Both remain hidden and neither is main/key.
        WindowPlacement secondaryOriginal;RememberWindow(secondary,secondaryOriginal);
        NSRect secondaryFrame=NSMakeRect(1920,-200,3840,2160);
        FillScreen(secondary,secondaryFrame);
        assert(NSEqualRects(secondary.frame,secondaryFrame));
        assert(NSEqualSizes(secondary.contentView.bounds.size,secondaryFrame.size));
        assert(!secondary.canBecomeKeyWindow && !secondary.canBecomeMainWindow);
        assert(!(secondary.styleMask&NSWindowStyleMaskTitled));
        RestoreWindow(secondary,secondaryOriginal);
        assert(secondary.styleMask&NSWindowStyleMaskTitled);
        [secondary close];
        playerWindow=nil;
        RaceBroadcastWindow* broadcast=[[RaceBroadcastWindow alloc] initWithContentRect:initial
            styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskResizable|NSWindowStyleMaskFullSizeContentView
            backing:NSBackingStoreBuffered defer:NO];
        broadcast.releasedWhenClosed=NO; broadcastWindow=broadcast;
        PlaceBroadcastWindow(broadcast,NSScreen.mainScreen);
        assert(NSEqualRects(broadcast.frame,NSScreen.mainScreen.frame));
        assert(NSEqualRects([broadcast constrainFrameRect:NSScreen.mainScreen.frame toScreen:NSScreen.mainScreen],NSScreen.mainScreen.frame));
        assert(NSEqualSizes(broadcast.contentView.bounds.size,NSScreen.mainScreen.frame.size));
        assert(broadcast.movableByWindowBackground && !broadcast.canBecomeKeyWindow);
        assert(broadcast.collectionBehavior & NSWindowCollectionBehaviorCanJoinAllSpaces);
        assert(broadcast.collectionBehavior & NSWindowCollectionBehaviorFullScreenAuxiliary);
        assert(!(broadcast.styleMask & NSWindowStyleMaskTitled));
        assert(broadcast.level>NSMainMenuWindowLevel && broadcast.hidesOnDeactivate);
        assert(FindUnityWindow()==primary);
        NSRect moved=NSMakeRect(initial.origin.x+25,initial.origin.y+35,800,450);
        [broadcast setFrame:moved display:NO];
        RefreshExternalLayout();
        assert(NSEqualRects(broadcast.frame,moved)); // P1/P2 auto placement must never undo manual broadcast movement.
        [broadcast close]; broadcastWindow=nil;
        [primary close];
        unityWindow=nil;
        TestIndependentReconnect(initial);
        std::cout<<"EXTERNAL_WINDOW_LAYOUT_PASS: production AppKit helpers; full content frames, portrait/negative coordinates, focus eligibility, idempotent sizing and original-window restoration. Windows remained hidden.\n";
    }
}
