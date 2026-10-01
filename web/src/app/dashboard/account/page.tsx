import { redirect } from "next/navigation";
import { currentUser } from "@/lib/backend";
import { AccountForm } from "@/features/account/account-form";

export default async function AccountPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  return <div className="workspace-body"><span className="eyebrow">{user.language === "sq" ? "LLOGARIA" : "ACCOUNT"}</span>
    <h1>{user.language === "sq" ? "Profili dhe siguria" : "Profile and security"}</h1>
    <AccountForm key={user.version} initial={user}/>
  </div>;
}
