#pragma once

#include "AppearancePolicy.hpp"

namespace nl::replication {
// Local, game-thread-only bookkeeping. Proxy IDs and tickets never come from
// peers and never go on the wire. This does not itself mutate game objects.
struct AppearanceTicket {
    uint64_t proxy = 0, serial = 0, catalogEpoch = 0;
    uint32_t generation = 0, revision = 0;
};

class AppearanceStaging {
    uint64_t proxy_ = 0, serial_ = 0, authorized_ = 0, catalogEpoch_ = 0;
    uint32_t generation_ = 0, applied_ = 0, pending_ = 0, highest_ = 0;

    void invalidate() { if (++serial_ == 0) ++serial_; pending_ = 0; authorized_ = 0; }
public:
    // Call on proxy creation, replacement, world change, or disconnect.
    void bind(uint64_t proxy, uint32_t generation) {
        invalidate(); proxy_ = proxy; generation_ = generation; applied_ = 0; highest_ = 0; catalogEpoch_ = 0;
        if (!proxy_ || !generation_) { proxy_ = 0; generation_ = 0; }
    }
    void clear() { bind(0, 0); }

    // Only a policy-approved value may be staged. A newer request invalidates
    // older resource-load callbacks; the approved value itself stays owned by
    // the caller until its asynchronous loads finish or the request is cancelled.
    bool stage(const ApprovedAppearance& approved, uint64_t catalogEpoch, AppearanceTicket& ticket) {
        ticket = {};
        if (!proxy_ || !generation_ || !catalogEpoch || !approved.ready() || approved.catalogEpoch() != catalogEpoch) return false;
        const auto& value = approved.value();
        if (value.generation != generation_ || value.revision <= applied_ || value.revision <= pending_ || value.revision < highest_) return false;
        invalidate(); pending_ = value.revision; highest_ = value.revision; catalogEpoch_ = catalogEpoch;
        ticket = {proxy_, serial_, catalogEpoch_, generation_, pending_}; return true;
    }
    bool canApply(const AppearanceTicket& ticket, uint64_t currentProxy, uint32_t currentGeneration,
                  uint64_t currentCatalogEpoch, bool allResourcesReady) const {
        return allResourcesReady && proxy_ && generation_ && pending_ && ticket.serial == serial_ &&
            ticket.proxy == proxy_ && currentProxy == proxy_ && ticket.generation == generation_ &&
            currentGeneration == generation_ && ticket.revision == pending_ && catalogEpoch_ &&
            ticket.catalogEpoch == catalogEpoch_ && currentCatalogEpoch == catalogEpoch_;
    }
    bool authorize(const AppearanceTicket& ticket, uint64_t currentProxy, uint32_t currentGeneration,
                   uint64_t currentCatalogEpoch, bool allResourcesReady) {
        if (!canApply(ticket, currentProxy, currentGeneration, currentCatalogEpoch, allResourcesReady)) return false;
        authorized_ = serial_; return true;
    }
    // Cancel a pending request even when its local resources became stale.
    // An obsolete callback cannot cancel a newer request.
    bool cancel(const AppearanceTicket& ticket) {
        if (!pending_ || ticket.serial != serial_ || ticket.proxy != proxy_ ||
            ticket.generation != generation_ || ticket.revision != pending_ || ticket.catalogEpoch != catalogEpoch_) return false;
        invalidate(); return true;
    }
    // Invoke only after the engine adapter finishes or aborts a staged attempt.
    // The adapter must arrange rollback/fallback itself if a game write fails.
    bool finish(const AppearanceTicket& ticket, uint64_t currentProxy, uint32_t currentGeneration,
                uint64_t currentCatalogEpoch, bool success) {
        if (!canApply(ticket, currentProxy, currentGeneration, currentCatalogEpoch, true) || (success && authorized_ != serial_)) return false;
        if (success) applied_ = pending_;
        invalidate(); return true;
    }
};
} // namespace nl::replication
