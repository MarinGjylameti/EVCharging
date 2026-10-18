import { useState } from "react";
import { Linking, Text, View } from "react-native";
import type { Language } from "../../contracts/account";
import { storeLinks } from "../config/store-links.cjs";
import { Button, Notice, styles } from "./ui";

export function PrivacyLinks({ language, account = false }: { language: Language; account?: boolean }) {
  const sq = language === "sq";
  const [error, setError] = useState("");
  // Expo replaces these explicit public environment references in the bundle.
  const links = storeLinks({
    EXPO_PUBLIC_PRIVACY_URL: process.env.EXPO_PUBLIC_PRIVACY_URL,
    EXPO_PUBLIC_ACCOUNT_DELETION_URL: process.env.EXPO_PUBLIC_ACCOUNT_DELETION_URL,
  });
  async function open(url: string) {
    setError("");
    try { await Linking.openURL(url); }
    catch { setError(sq ? "Faqja nuk u hap. Kontrollo lidhjen dhe provo përsëri." : "The page could not be opened. Check your connection and try again."); }
  }
  if (!links.privacy && (!account || !links.deletion)) return null;
  return <View style={{ gap: 8 }}>
    {account && <Text style={styles.label}>{sq ? "Privatësia dhe llogaria" : "Privacy and account"}</Text>}
    {links.privacy && <Button title={sq ? "Politika e privatësisë ↗" : "Privacy policy ↗"} secondary onPress={() => void open(links.privacy)}/>}
    {account && links.deletion && <>
      <Button title={sq ? "Kërko fshirjen e llogarisë ↗" : "Request account deletion ↗"} secondary onPress={() => void open(links.deletion)}/>
      <Text style={styles.body}>{sq ? "Hapet faqja e operatorit për të kërkuar fshirjen e llogarisë dhe të dhënave. Hapja e faqes nuk e fshin llogarinë." : "Opens the operator's page to request deletion of your account and data. Opening the page does not delete your account."}</Text>
    </>}
    <Notice text={error}/>
  </View>;
}
