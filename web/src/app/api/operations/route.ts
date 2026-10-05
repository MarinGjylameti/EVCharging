import { NextResponse } from "next/server";
import { backend, sameOrigin, sessionCookie } from "@/lib/backend";
import { boundedJson } from "@/lib/request-body";

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const resources = new Set(["stations","locations","administrators","drivers","summary","sessions","audit","prices","notifications"]);
async function proxy(path:string, init:RequestInit = {}) {
  try {
    const response = await backend(path,init);
    const result = NextResponse.json(await response.json().catch(()=>null),{status:response.status});
    result.headers.set("Cache-Control","no-store");
    if(response.status===401) result.cookies.delete(sessionCookie);
    return result;
  } catch {return NextResponse.json({code:"service_unavailable"},{status:503});}
}
export async function GET(request:Request) {
  const params = new URL(request.url).searchParams;
  const resource = params.get("resource") ?? "";
  if(resource === "device") {
    const id=params.get("id");
    if(!id || !guid.test(id)) return NextResponse.json({code:"invalid_resource"},{status:400});
    return proxy(`/admin/stations/${id}/device`,{signal:request.signal});
  }
  if(!resources.has(resource)) return NextResponse.json({code:"invalid_resource"},{status:400});
  if(resource==="notifications") return proxy("/me/notifications",{signal:request.signal});
  const query=new URLSearchParams();
  for(const key of ["stationId","locationId","status","from","until","driver","page"]) {
    const value=params.get(key);
    if(value!==null) {
      if(value.length>254 || key.endsWith("Id")&&!guid.test(value)) return NextResponse.json({code:"invalid_filter"},{status:400});
      query.set(key,value);
    }
  }
  return proxy(`/admin/${resource}?${query}`,{signal:request.signal});
}
export async function POST(request:Request) {
  if(!sameOrigin(request)) return NextResponse.json({code:"invalid_origin"},{status:403});
  let body:Record<string,unknown>;
  try {body=await boundedJson(request);} catch {return NextResponse.json({code:"invalid_body"},{status:400});}
  const {resource,operation,id,input}=body;
  if(!input || typeof input!=="object" || Array.isArray(input)) return NextResponse.json({code:"invalid_body"},{status:400});
  const validId=typeof id==="string"&&guid.test(id);
  let path:string; let method="POST";
  if(resource==="device" && validId && (operation==="provision"||operation==="control"||operation==="assignDriver")) {
    path=`/admin/stations/${id}/device`; if(operation==="control") method="PATCH";
    if(operation==="assignDriver") {path+="/driver";method="PUT";}
  } else if((resource==="locations"||resource==="administrators") && (operation==="create" || validId&&(operation==="update"||operation==="archive"))) {
    path=`/admin/${resource}${operation==="create"?"":`/${id}`}${operation==="archive"?"/archive":""}`;
    if(operation==="update") method="PATCH";
  } else return NextResponse.json({code:"invalid_operation"},{status:400});
  return proxy(path,{method,body:JSON.stringify(input),signal:request.signal});
}
