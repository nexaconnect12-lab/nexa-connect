import { ApiError } from "@nexaconnect/api-client";
import { Component, type ErrorInfo, type ReactNode } from "react";

export interface DisplayError { kind: "unauthorized" | "forbidden" | "validation" | "network" | "unexpected"; message: string; retryable: boolean; fieldErrors?: Record<string, string[]>; }

export function normalizeError(error: unknown): DisplayError {
  if (error instanceof ApiError) {
    if (error.status === 401) return { kind: "unauthorized", message: "Your session has expired.", retryable: false };
    if (error.status === 403) return { kind: "forbidden", message: "You do not have access to this action.", retryable: false };
    if (error.status === 400 && error.problem?.errors) return { kind: "validation", message: error.problem.title ?? "Check the form and try again.", retryable: false, fieldErrors: error.problem.errors };
    return { kind: "unexpected", message: error.problem?.title ?? "The request could not be completed.", retryable: error.status >= 500 };
  }
  if (error instanceof TypeError) return { kind: "network", message: "The service could not be reached.", retryable: true };
  return { kind: "unexpected", message: "Something went wrong.", retryable: false };
}

export class ErrorBoundary extends Component<{ fallback: (error: DisplayError) => ReactNode; onError?: (error: unknown, info: ErrorInfo) => void; children: ReactNode }, { error?: unknown }> {
  state: { error?: unknown } = {};
  static getDerivedStateFromError(error: unknown) { return { error }; }
  componentDidCatch(error: unknown, info: ErrorInfo) { this.props.onError?.(error, info); }
  render() { return this.state.error ? this.props.fallback(normalizeError(this.state.error)) : this.props.children; }
}
