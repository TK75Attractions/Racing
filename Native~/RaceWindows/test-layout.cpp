#include "ExternalDisplayLayout.h"
#include <cassert>
#include <iostream>
#include <limits>

using racing::DisplayArea;
static void Expect(std::vector<DisplayArea> displays, uint32_t p1, uint32_t p2)
{
    auto pair=racing::ExternalPair(displays);
    if (!p1) { assert(pair[0]==-1 && pair[1]==-1); return; }
    assert(displays[pair[0]].id==p1 && displays[pair[1]].id==p2);
    assert(!displays[pair[0]].builtIn && !displays[pair[1]].builtIn);
    assert(pair[0]!=pair[1]);
}
int main()
{
    Expect({},0,0);
    Expect({{1,true,false,0,0,1440,900}},0,0);
    Expect({{2,false,false,-1920,0,1920,1080},{1,true,false,0,0,1440,900}},0,0);
    Expect({{1,true,false,0,0,1440,900},{2,false,false,-1920,0,1920,1080},{3,false,false,1440,0,2560,1440}},2,3);
    // macOS main-screen enumeration is independent of the built-in screen flag and physical ordering.
    Expect({{3,false,false,0,0,2560,1440},{1,true,false,-1440,0,1440,900},{2,false,false,-3360,0,1920,1080}},2,3);
    Expect({{5,false,false,0,0,1920,1080},{4,false,false,-2560,0,2560,1440}},4,5); // closed lid / desktop Mac
    Expect({{2,false,false,0,0,1920,1080},{3,false,true,1920,0,1920,1080}},0,0);
    Expect({{2,false,false,0,0,1920,1080},{2,false,false,1920,0,1920,1080}},0,0);
    Expect({{2,false,false,0,0,1920,1080},{3,false,false,0,1080,1080,1920}},3,2); // stacked portrait
    Expect({{4,false,false,1920,0,1920,1080},{2,false,false,-1920,0,1920,1080},{3,false,false,0,0,1920,1080}},2,3);
    Expect({{9,false,false,0,0,0,1080},{8,false,false,std::numeric_limits<double>::quiet_NaN(),0,1920,1080},{2,false,false,0,0,1920,1080}},0,0);
    // Equal-origin displays are deterministic even if enumeration order changes.
    Expect({{3,false,false,0,0,1920,1080},{2,false,false,0,0,1920,1080}},2,3);
    assert(racing::BroadcastTarget({},1)==-1);
    assert(racing::BroadcastTarget({{3,false,false,0,0,1920,1080},{1,true,false,-1440,0,1440,900},{2,false,false,-3360,0,1920,1080}},3)==1);
    assert(racing::BroadcastTarget({{2,false,false,-1920,0,1920,1080},{3,false,false,0,0,1920,1080}},3)==1);
    assert(racing::BroadcastTarget({{1,true,true,0,0,1440,900},{3,false,false,0,0,1920,1080}},3)==1);
    assert(racing::BroadcastTarget({{3,false,false,0,0,1920,1080}},99)==0);
    std::cout<<"BROADCAST_DISPLAY_SELECTION_PASS: built-in before player/main order, desktop main fallback, mirrored and absent panels.\n";
    std::cout<<"EXTERNAL_DISPLAY_SELECTION_PASS: 12 configurations; built-in/main ordering, lid closed, portrait, negative coordinates, mirroring, duplicates, invalid geometry.\n";
}
