import { createContext, type PropsWithChildren, type ReactNode, useContext } from "react";

export type UiCapability = string;
export type UiAuthorizationEvaluator = (capability: UiCapability) => boolean;

const AuthorizationContext = createContext<UiAuthorizationEvaluator | undefined>(undefined);

export interface AuthorizationUiProviderProps extends PropsWithChildren {
  can: UiAuthorizationEvaluator;
}

/** Presentation hint only. Every BFF and service must independently authorize requests. */
export function AuthorizationUiProvider({ can, children }: AuthorizationUiProviderProps) {
  return <AuthorizationContext.Provider value={can}>{children}</AuthorizationContext.Provider>;
}

export function useCan(capability: UiCapability): boolean {
  return useAuthorizationUi()(capability);
}

export function useAuthorizationUi(): UiAuthorizationEvaluator {
  const evaluator = useContext(AuthorizationContext);
  if (!evaluator) throw new Error("Authorization UI helpers must be used inside an AuthorizationUiProvider");
  return evaluator;
}

export interface AuthorizedProps extends PropsWithChildren {
  capability: UiCapability;
  fallback?: ReactNode;
}

export function Authorized({ capability, fallback = null, children }: AuthorizedProps) {
  return useCan(capability) ? <>{children}</> : <>{fallback}</>;
}

export function createCapabilityEvaluator(capabilities: ReadonlySet<string>): UiAuthorizationEvaluator {
  return capability => capabilities.has(capability);
}
