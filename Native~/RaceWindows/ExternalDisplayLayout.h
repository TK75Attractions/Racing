#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <set>
#include <vector>

namespace racing {
struct DisplayArea {
    uint32_t id;
    bool builtIn, mirrored;
    double x, y, width, height; // AppKit points, never Retina backing pixels.
};
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
}
