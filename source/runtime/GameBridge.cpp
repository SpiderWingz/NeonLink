// RED4ext SDK engine adapter for Cyberpunk 2.5. These functions are called
// synchronously by redscript. No game object or handle crosses a worker thread.
#include <windows.h>
#include <intrin.h>
#if defined(__MINGW32__)
#define InterlockedExchange8 _InterlockedExchange8
#endif
#include <RED4ext/RED4ext.hpp>
#include "Replication.hpp"
#include <atomic>
#include "Diagnostics.hpp"

namespace nl {
extern replication::Mailbox movement;
extern std::atomic<bool> movementBridge;
extern std::atomic<bool> previewMode, peerConnected;
extern std::atomic<int> avatarStage;
extern std::atomic<ULONGLONG> avatarUpdated;
namespace bridge {
thread_local replication::Pose polled;
void publish(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* result, int64_t) {
    RED4ext::Vector4 position;
    RED4ext::Quaternion rotation;
    RED4ext::GetParameter(frame, &position);
    RED4ext::GetParameter(frame, &rotation);
    frame->code++;
    replication::Pose pose;
    pose.position = {position.X, position.Y, position.Z};
    pose.rotation = {rotation.i, rotation.j, rotation.k, rotation.r};
    const bool accepted = movement.publish(pose, GetTickCount64());
    if (result) *result = accepted;
}
void poll(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* result, int64_t) {
    frame->code++;
    polled = {};
    const bool available = movement.sample(GetTickCount64(), polled);
    if (result) *result = available;
}
void position(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, RED4ext::Vector4* result, int64_t) {
    frame->code++;
    if (result) { result->X = polled.position[0]; result->Y = polled.position[1]; result->Z = polled.position[2]; result->W = 1; }
}
void rotation(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, RED4ext::Quaternion* result, int64_t) {
    frame->code++;
    if (result) *result = {polled.rotation[0], polled.rotation[1], polled.rotation[2], polled.rotation[3]};
}
void generation(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, uint32_t* result, int64_t) {
    frame->code++;
    if (result) *result = polled.generation;
}
void clear(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, void*, int64_t) {
    frame->code++; movement.clearLocal(); polled = {}; avatarStage = 0; avatarUpdated = 0;
}
void isPreview(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* result, int64_t) {
    frame->code++; if (result) *result = previewMode.load();
}
void isConnected(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, bool* result, int64_t) {
    frame->code++; if (result) *result = peerConnected.load();
}
void report(RED4ext::IScriptable*, RED4ext::CStackFrame* frame, void*, int64_t) {
    int32_t stage = 0; RED4ext::GetParameter(frame, &stage); frame->code++;
    if (stage < 0 || stage > 5) return;
    if (avatarStage.exchange(stage) != stage) diagnostics::write("Partner adapter stage: " + std::to_string(stage));
    avatarUpdated = GetTickCount64();
}
void registerFunctions() {
    auto rtti = RED4ext::CRTTISystem::Get();
    RED4ext::CBaseFunction::Flags flags{}; flags.isNative = true; flags.isStatic = true;
    auto add = [&](const char* name, RED4ext::ScriptingFunction_t<void*> callback, const char* returnType) {
        auto function = RED4ext::CGlobalFunction::Create(name, name, callback);
        function->flags = flags;
        if (returnType && !function->SetReturnType(returnType)) return static_cast<RED4ext::CGlobalFunction*>(nullptr);
        return function;
    };
    auto publishFunction = add("NeonLinkPublish", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&publish), "Bool");
    if (!publishFunction || !publishFunction->AddParam("Vector4", "position") || !publishFunction->AddParam("Quaternion", "rotation")) return;
    auto reportFunction = add("NeonLinkReport", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&report), nullptr);
    if (!reportFunction || !reportFunction->AddParam("Int32", "stage")) return;
    std::array<RED4ext::CGlobalFunction*, 9> functions{
        publishFunction,
        add("NeonLinkPoll", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&poll), "Bool"),
        add("NeonLinkPosition", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&position), "Vector4"),
        add("NeonLinkRotation", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&rotation), "Quaternion"),
        add("NeonLinkGeneration", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&generation), "Uint32"),
        add("NeonLinkClear", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&clear), nullptr),
        add("NeonLinkIsPreview", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&isPreview), "Bool"),
        add("NeonLinkPeerConnected", reinterpret_cast<RED4ext::ScriptingFunction_t<void*>>(&isConnected), "Bool"),
        reportFunction
    };
    for (auto function : functions) if (!function) return;
    for (auto function : functions) rtti->RegisterFunction(function);
    movementBridge = true;
    diagnostics::write("Movement native functions registered.");
}
bool initialize(RED4ext::v1::PluginHandle handle, const RED4ext::v1::Sdk* sdk) {
    // Test harnesses provide no engine module and must never resolve addresses.
    if (!GetModuleHandleW(L"RED4ext.dll")) return true;
    wchar_t path[32768]{};
    const DWORD length = GetModuleFileNameW(handle, path, 32768);
    if (!length || length >= 32768 || !sdk->scripts || !sdk->scripts->Add) return false;
    auto scripts = std::filesystem::path(path).parent_path() / L"Scripts";
    if (!sdk->scripts->Add(handle, scripts.c_str())) return false;
    RED4ext::CRTTISystem::Get()->AddPostRegisterCallback(registerFunctions);
    return true;
}
} // namespace bridge
} // namespace nl
