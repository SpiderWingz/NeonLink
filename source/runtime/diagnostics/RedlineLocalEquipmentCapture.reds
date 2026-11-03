// Local, read-only experiment. It does not serialize or apply equipment.
public struct RedlineLocalEquipmentSample {
    public let area: gamedataEquipmentArea;
    public let slot: Int32;
    public let record: TweakDBID;
    public let appearance: CName;
}

public class RedlineLocalEquipmentSnapshot extends IScriptable {
    public let items: array<RedlineLocalEquipmentSample>;
}

public abstract class RedlineLocalEquipmentCapture {
    // Must run on a verified game thread for the current local player only.
    // These records describe base equipment, not inventory instances or stats.
    public static func Capture(player: ref<PlayerPuppet>) -> ref<RedlineLocalEquipmentSnapshot> {
        let data: ref<EquipmentSystemPlayerData>;
        let snapshot: ref<RedlineLocalEquipmentSnapshot>;
        let areas: array<gamedataEquipmentArea>;
        let item: ItemID;
        let record: wref<Item_Record>;
        let sample: RedlineLocalEquipmentSample;
        let i: Int32 = 0;
        let slot: Int32;
        let count: Int32;
        let visibleWhenLocked: Bool;
        if !IsDefined(player) || player != GetPlayer(GetGameInstance()) { return null; }
        data = EquipmentSystem.GetData(player);
        if !IsDefined(data) { return null; }
        areas = [gamedataEquipmentArea.Head, gamedataEquipmentArea.Face, gamedataEquipmentArea.InnerChest,
            gamedataEquipmentArea.OuterChest, gamedataEquipmentArea.Legs, gamedataEquipmentArea.Feet,
            gamedataEquipmentArea.Outfit, gamedataEquipmentArea.ArmsCW];
        snapshot = new RedlineLocalEquipmentSnapshot();
        while i < ArraySize(areas) {
            if data.GetEquipAreaIndex(areas[i]) < 0 { return null; }
            count = data.GetNumberOfSlots(areas[i], true);
            if count < 0 || count > 16 { return null; }
            slot = 0;
            while slot < count {
                item = data.GetItemInEquipSlot(areas[i], slot);
                if !data.IsSlotLocked(areas[i], slot, visibleWhenLocked) && ItemID.IsValid(item) {
                    if ArraySize(snapshot.items) >= 64 { return null; }
                    record = TweakDBInterface.GetItemRecord(ItemID.GetTDBID(item));
                    if !IsDefined(record) { return null; }
                    sample.area = areas[i]; sample.slot = slot; sample.record = ItemID.GetTDBID(item);
                    sample.appearance = record.AppearanceName();
                    ArrayPush(snapshot.items, sample);
                }
                slot += 1;
            }
            i += 1;
        }
        if player != GetPlayer(GetGameInstance()) { return null; }
        return snapshot;
    }
}
