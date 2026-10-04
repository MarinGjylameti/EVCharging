import { cookies } from "next/headers";
import { apiOrigin, sessionCookie } from "@/lib/backend";
export const dynamic = "force-dynamic";
export async function GET(request:Request) {
  const token=(await cookies()).get(sessionCookie)?.value;
  if(!token) return new Response(null,{status:401});
  try {
    const response=await fetch(`${apiOrigin()}/api/v1/admin/events`,{headers:{Authorization:`Bearer ${token}`},cache:"no-store",signal:request.signal});
    if(!response.ok) return new Response(null,{status:response.status});
    return new Response(response.body,{headers:{"Content-Type":"text/event-stream","Cache-Control":"no-store, no-transform","X-Accel-Buffering":"no","Content-Encoding":"identity"}});
  } catch {return new Response(null,{status:503});}
}
