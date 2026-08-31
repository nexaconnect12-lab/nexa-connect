export type Access = { organizationId:string; organizationCode:string; organizationName:string; applicationCode:string };
export type Tenant = { subjectId:string; organizationId:string; applicationCode:string };
export type PortalSection = "profile"|"products"|"users"|"configuration"|"branches"|"dashboards"|"reports"|"media"|"activity"|"payment-reviews";
export const sections:readonly PortalSection[]=["profile","products","users","configuration","branches","dashboards","reports","media","activity","payment-reviews"];
export function matchesTenant(access:Access,tenant:Tenant){return access.organizationId===tenant.organizationId&&access.applicationCode===tenant.applicationCode;}
export function selectedAccess(access:readonly Access[],tenant?:Tenant){return tenant?access.find(item=>matchesTenant(item,tenant)):undefined;}
export function scopedFeaturePath(section:Exclude<PortalSection,"profile"|"products"|"dashboards">){return `/bff/customer/features/${section}`;}
