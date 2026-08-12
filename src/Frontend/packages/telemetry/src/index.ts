export type TelemetryValue = string | number | boolean;
export type TelemetryAttributes = Readonly<Record<string, TelemetryValue>>;
export interface TelemetryEvent { name: string; timestamp: string; attributes: TelemetryAttributes; }
export interface TelemetrySink { emit(event: TelemetryEvent): void; }

const forbiddenAttribute = /token|cookie|secret|password|authorization|body|email|phone|card/i;

export function sanitizeAttributes(attributes: TelemetryAttributes): TelemetryAttributes {
  return Object.fromEntries(Object.entries(attributes).filter(([key]) => !forbiddenAttribute.test(key)));
}

export function createTelemetry(serviceName: string, sink: TelemetrySink, now = () => new Date()) {
  const emit = (name: string, attributes: TelemetryAttributes = {}) => sink.emit({
    name,
    timestamp: now().toISOString(),
    attributes: sanitizeAttributes({ ...attributes, service: serviceName })
  });
  return {
    event: emit,
    pageView: (routeTemplate: string) => emit("ui.page_view", { route: routeTemplate }),
    failure: (operation: string, errorType: string) => emit("ui.failure", { operation, errorType })
  };
}

export function createCorrelationId(): string {
  return globalThis.crypto.randomUUID();
}
