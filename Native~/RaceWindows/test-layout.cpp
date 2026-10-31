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
    racing::ExternalDisplayAssignment assignment;
    DisplayArea p1={20,false,false,-1920,0,1920,1080,"seat-one"};
    DisplayArea p2={30,false,false,1440,0,2560,1440,"seat-two"};
    DisplayArea builtIn={10,true,false,0,0,1440,900,"operator"};
    DisplayArea replacement={40,false,false,-3840,0,1920,1080,"unrelated"};
    assert((assignment.Resolve({builtIn,p1})==std::array<int,2>{-1,-1}));
    assert(!assignment.Assigned());
    assert((assignment.Resolve({builtIn,p2,p1})==std::array<int,2>{2,1}));
    assert(assignment.Assigned());
    for (int repeat=0;repeat<100;++repeat) {
        assert((assignment.Resolve({p2,builtIn})==std::array<int,2>{-1,0}));
        assert((assignment.Resolve({replacement,p2,builtIn})==std::array<int,2>{-1,1}));
        assert((assignment.Resolve({p1,builtIn})==std::array<int,2>{0,-1}));
        assert((assignment.Resolve({})==std::array<int,2>{-1,-1}));
        // Display IDs, enumeration, physical ordering and resolution may all change on reconnect.
        p1.id=200; p1.x=4000; p2.id=300; p2.x=-2560; p2.width=3840;
        assert((assignment.Resolve({p2,p1,builtIn})==std::array<int,2>{1,0}));
    }
    p1.mirrored=true;
    assert((assignment.Resolve({p1,p2})==std::array<int,2>{-1,1}));
    p1.mirrored=false; p1.width=0;
    assert((assignment.Resolve({p1,p2})==std::array<int,2>{-1,1}));
    p1.width=1920;
    assert((assignment.Resolve({p1,p2})==std::array<int,2>{0,1}));
    assert(racing::FindDisplay({builtIn,p1,p2},"operator")==0);
    DisplayArea duplicate=p1; duplicate.id=999;
    assert(racing::FindDisplay({p1,duplicate},"seat-one")==-1);
    assignment.Reset();
    assert((assignment.Resolve({replacement,p2})==std::array<int,2>{0,1}));
    std::cout<<"DISPLAY_RECONNECT_ASSIGNMENT_PASS: stable UUIDs; independent/repeated loss, both absent, unrelated replacement, enumeration/coordinate/ID changes, mirroring, invalid and ambiguous screens.\n";
    std::cout<<"BROADCAST_DISPLAY_SELECTION_PASS: built-in before player/main order, desktop main fallback, mirrored and absent panels.\n";
    std::cout<<"EXTERNAL_DISPLAY_SELECTION_PASS: 12 configurations; built-in/main ordering, lid closed, portrait, negative coordinates, mirroring, duplicates, invalid geometry.\n";
}
