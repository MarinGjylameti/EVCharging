export async function operations<T>(resource:string,signal?:AbortSignal,query=""):Promise<T> {
  const timeout=AbortSignal.timeout(15000);
  const response=await fetch(`/api/operations?resource=${resource}${query}`,{cache:"no-store",signal:signal?AbortSignal.any([signal,timeout]):timeout});
  if(response.status===401||response.status===403) {window.location.assign("/"); throw new Error("Sign in again.");}
  if(!response.ok) {const data=await response.json().catch(()=>({}));throw new Error(data.code??"service_unavailable");}
  return response.json();
}
export async function mutate<T>(resource:string,operation:string,input:unknown,id?:string):Promise<T> {
  const response=await fetch("/api/operations",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({resource,operation,id,input})});
  if(response.status===401) {window.location.assign("/");throw new Error("Sign in again.");}
  const data=await response.json(); if(!response.ok) throw new Error(data.code??"request_failed"); return data;
}
export function failure(error:unknown,sq:boolean):string {
  const code=error instanceof Error?error.message:"request_failed";
  const messages:Record<string,[string,string]>={
    invalid_device_identity:["Shkruaj Charge Point ID të plotë: 2–40 shkronja, numra, _ ose -.","Enter the complete Charge Point ID: 2–40 letters, digits, _ or -."],
    invalid_log_filter:["Zgjidh OCPP ose HTTP dhe një ID deri në 40 karaktere.","Choose OCPP or HTTP and an ID up to 40 characters."],
    invalid_card_input:["Shkruaj kodin e kartës (1–20 karaktere) ose JSON të vlefshëm me idTag tekst.","Enter the card code (1–20 characters) or valid JSON with a string idTag."],
    driver_required:["Zgjidh përdoruesin që ka këtë kartë.","Choose the account that owns this card."],
    invalid_driver:["Zgjidh një llogari Driver aktive.","Choose an active Driver account."],
    version_conflict:["Të dhënat kanë ndryshuar. Rifresko para se të ruash.","The resource changed. Refresh before saving."],
    location_has_stations:["Zhvendos ose arkivo pikat aktive përpara vendndodhjes.","Move or archive active stations first."],
    station_busy:["Pika ka një karikim aktiv. Përfundoje përpara këtij veprimi.","Finish the active charging session first."],
    email_unavailable:["Ky email është në përdorim.","This email is already used."],
    invalid_assignments:["Cakto vetëm pika dhe vendndodhje aktive.","Assign only active stations and locations."],
    access_denied:["Nuk ke leje për këtë veprim.","You do not have access to this action."],
    device_already_provisioned:["Pika ose ID-ja ka tashmë një pajisje të regjistruar.","This station or identity is already provisioned."],
    reserved_device_identity:["Përdor një ID tjetër; SIM- ruhet për simulatorët.","Use a different identity; SIM- is reserved for simulators."],
    invalid_device_details:["Kontrollo ID-në, serialin dhe identifikuesit e autorizuar.","Check the identity, serial and authorized identifiers."]
  };
  return messages[code]?.[sq?0:1]??(sq?`Veprimi dështoi (${code}). Kontrollo të dhënat dhe provo përsëri.`:`Request failed (${code}). Check the details and retry.`);
}
