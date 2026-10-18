import { Platform } from "react-native";
import Constants from "expo-constants";
import * as Device from "expo-device";
import * as Notifications from "expo-notifications";
import { request } from "./api";

Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldPlaySound: true, shouldSetBadge: false, shouldShowBanner: true, shouldShowList: true })
});

export async function registerPush(token: string, askPermission: boolean) {
  const projectId = Constants.easConfig?.projectId ?? Constants.expoConfig?.extra?.eas?.projectId;
  if (!Device.isDevice || typeof projectId !== "string" || !/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(projectId) || Constants.appOwnership === "expo") {
    if (askPermission) throw new Error("pushSetup");
    return;
  }
  if (Platform.OS === "android") await Notifications.setNotificationChannelAsync("charging", {
    name: "Charging / Karikimi", importance: Notifications.AndroidImportance.DEFAULT
  });
  let permission = await Notifications.getPermissionsAsync();
  const allowed = () => permission.granted || permission.ios?.status === Notifications.IosAuthorizationStatus.PROVISIONAL;
  if (!allowed() && askPermission && permission.canAskAgain) permission = await Notifications.requestPermissionsAsync();
  if (!allowed()) {
    if (askPermission) throw new Error("pushDenied");
    return;
  }
  const expoToken = await Notifications.getExpoPushTokenAsync({ projectId });
  await request("/me/devices", token, "POST", { token: expoToken.data, platform: Platform.OS });
}
