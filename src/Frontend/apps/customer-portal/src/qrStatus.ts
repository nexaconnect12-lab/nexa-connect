// Presentation only: service states and command eligibility remain authoritative.
export function qrStatus(value: {status:string;orderStatus?:string;collectionStatus?:string}): string {
 if(value.orderStatus?.toLowerCase()==="paid"||value.collectionStatus==="paid")return "Paid";
 if(value.status==="pending")return "Waiting for staff";
 if(value.status==="price_review")return "Review updated prices";
 if(value.status==="rejected")return "Declined by staff";
 if(value.status==="expired")return "Request expired";
 if(value.collectionStatus==="awaiting_cashier")return "Awaiting cashier";
 if(value.collectionStatus==="collecting")return "At cashier";
 if(value.collectionStatus==="unavailable")return "Ask staff for help";
 return "Preparing";
}
