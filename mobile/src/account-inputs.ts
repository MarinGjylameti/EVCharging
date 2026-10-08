import type { User } from "../../contracts/account";

export const profileDraft = (user: User) => ({
  firstName: user.firstName, lastName: user.lastName, phone: user.phone ?? "",
  vehicleMake: user.vehicleMake ?? "", vehicleModel: user.vehicleModel ?? "",
  batteryType: user.batteryType ?? "", batteryCapacityKWh: user.batteryCapacityKWh?.toString() ?? "",
});
export function profilePayload(draft: ReturnType<typeof profileDraft>, version: number) {
  const input = draft.batteryCapacityKWh.trim().replace(",", ".");
  const capacity = input ? Number(input) : null;
  if (capacity !== null && (!/^\d+(\.\d{1,2})?$/.test(input) || !Number.isFinite(capacity) || capacity <= 0 || capacity > 1000)) throw new Error("invalid_battery_capacity");
  if (!draft.firstName.trim() || !draft.lastName.trim()) throw new Error("invalid_name");
  return { ...Object.fromEntries(Object.entries(draft).map(([key, value]) => [key, value.trim()])), batteryCapacityKWh: capacity, version };
}
