import { sections, type PortalSection } from "./portal";

export function isKitchenEntry(pathname = location.pathname): boolean {
  return pathname === "/kitchen" || pathname === "/kitchen/";
}

export function currentSection(): PortalSection {
  if (isKitchenEntry()) return "kitchen";
  const section = location.hash.slice(1) as PortalSection;
  return sections.includes(section) ? section : "profile";
}

// The BFF validates local return URLs. Never carry tokens or queue commands here.
export function customerLoginUrl(): string {
  return `/bff/customer/login?returnUrl=${encodeURIComponent(location.pathname + location.search + location.hash)}`;
}

export function signInToCustomer(): void {
  location.assign(customerLoginUrl());
}
