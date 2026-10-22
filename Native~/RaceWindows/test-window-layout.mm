// The production helpers are compiled into a hidden-window AppKit host; no Unity or physical screen emulation.
#include "RaceWindows.mm"
#include <cassert>
#include <iostream>

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
        [primary close];
        unityWindow=nil;
        std::cout<<"EXTERNAL_WINDOW_LAYOUT_PASS: production AppKit helpers; full content frames, portrait/negative coordinates, focus eligibility, idempotent sizing and original-window restoration. Windows remained hidden.\n";
    }
}
