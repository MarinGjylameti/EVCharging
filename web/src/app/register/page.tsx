import { AuthPage } from "@/features/auth/auth-page";
import { LoginForm } from "@/features/auth/login-form";

export default function RegisterPage() {
  return <AuthPage><LoginForm register/></AuthPage>;
}
