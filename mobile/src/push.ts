import { Platform } from "react-native";
import Constants from "expo-constants";
import * as Device from "expo-device";
import * as Notifications from "expo-notifications";
import { request } from "./api";

Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldPlaySound: true, shouldSetBadge: false, shouldShowBanner: true, shouldShowList: true })
});

export async function registerPush(token: string, askPermission: boolean) {
  const projectId = Constants.expoConfig?.extra?.eas?.projectId as string | undefined;
  if (!Device.isDevice || !projectId || Constants.appOwnership === "expo") throw new Error("pushSetup");
  if (Platform.OS === "android") await Notifications.setNotificationChannelAsync("charging", {
    name: "Charging / Karikimi", importance: Notifications.AndroidImportance.DEFAULT
  });
  let permission = await Notifications.getPermissionsAsync();
  if (!permission.granted && askPermission) permission = await Notifications.requestPermissionsAsync();
  if (!permission.granted) throw new Error("pushDenied");
  const expoToken = await Notifications.getExpoPushTokenAsync({ projectId });
  await request("/me/devices", token, "POST", { token: expoToken.data, platform: Platform.OS });
}
