#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <set>
#include <string>
#include <vector>

namespace racing {
struct DisplayArea {
    uint32_t id;
    bool builtIn, mirrored;
    double x, y, width, height; // AppKit points, never Retina backing pixels.
    std::string identity{}; // Persistent display UUID; numeric IDs can change on reconnect.
};
inline bool Usable(const DisplayArea& d)
{
    return d.id && !d.mirrored && d.width>0 && d.height>0 &&
        std::isfinite(d.x) && std::isfinite(d.y) && std::isfinite(d.width) && std::isfinite(d.height);
}
inline std::string Identity(const DisplayArea& d)
{
    return d.identity.empty() ? "id:"+std::to_string(d.id) : d.identity;
}
inline int FindDisplay(const std::vector<DisplayArea>& displays, const std::string& identity)
{
    if (identity.empty()) return -1;
    int match=-1;
    for (int i=0;i<(int)displays.size();++i)
        if (Usable(displays[i]) && Identity(displays[i])==identity) {
            if (match>=0) return -1; // An ambiguous identity cannot safely take over a seat.
            match=i;
        }
    return match;
}
// The operator display is the built-in panel, independent of which player window is key.
inline int BroadcastTarget(const std::vector<DisplayArea>& displays, uint32_t mainId)
{
    int main=-1, first=-1;
    for(int i=0;i<(int)displays.size();++i) {
        const auto& d=displays[i];
        if(!d.id || d.mirrored || d.width<=0 || d.height<=0 || !std::isfinite(d.x) || !std::isfinite(d.y) || !std::isfinite(d.width) || !std::isfinite(d.height))continue;
        if(d.builtIn)return i;
        if(d.id==mainId)main=i;
        if(first<0)first=i;
    }
    return main>=0?main:first;
}
inline std::array<int, 2> ExternalPair(const std::vector<DisplayArea>& displays)
{
    std::vector<int> external;
    std::set<uint32_t> seen;
    for (int i = 0; i < (int)displays.size(); ++i) {
        const auto& d = displays[i];
        if (!d.id || d.builtIn || d.mirrored || d.width <= 0 || d.height <= 0 ||
            !std::isfinite(d.x) || !std::isfinite(d.y) || !std::isfinite(d.width) || !std::isfinite(d.height) || !seen.insert(d.id).second) continue;
        external.push_back(i);
    }
    std::sort(external.begin(), external.end(), [&](int a, int b) {
        const auto& left=displays[a]; const auto& right=displays[b];
        if (left.x != right.x) return left.x < right.x;
        if (left.y != right.y) return left.y > right.y;
        return left.id < right.id;
    });
    return external.size() >= 2 ? std::array<int,2>{external[0],external[1]} : std::array<int,2>{-1,-1};
}
// Choose a pair once. Losing one display must never hand its seat to another screen.
class ExternalDisplayAssignment {
    std::array<std::string,2> identities;
public:
    bool Assigned() const { return !identities[0].empty(); }
    void Reset() { identities={}; }
    std::array<int,2> Resolve(const std::vector<DisplayArea>& displays)
    {
        if (!Assigned()) {
            auto pair=ExternalPair(displays);
            if (pair[0]<0) return pair;
            for (int i=0;i<2;++i) identities[i]=Identity(displays[pair[i]]);
        }
        std::array<int,2> result;
        for (int i=0;i<2;++i) {
            int found=FindDisplay(displays,identities[i]);
            result[i]=found>=0 && !displays[found].builtIn ? found : -1;
        }
        return result;
    }
};
}
