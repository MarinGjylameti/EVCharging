import * as SecureStore from "expo-secure-store";
import { mapProviderFromPreference, type MapProvider } from "../../shared/map-directions";

const key = "evcharging.map-provider.v1";
const options = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };
export async function readMapProvider(): Promise<MapProvider> {
  return mapProviderFromPreference(await SecureStore.getItemAsync(key, options));
}
export async function saveMapProvider(provider: MapProvider) {
  await SecureStore.setItemAsync(key, provider, options);
}
