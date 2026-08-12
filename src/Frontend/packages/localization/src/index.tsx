import { createContext, type PropsWithChildren, useContext, useMemo } from "react";

export type Messages = Readonly<Record<string, string>>;
export interface LocaleDefinition { locale: string; messages: Messages; }
export interface Localization { locale: string; t: (key: string, values?: Record<string, string | number>) => string; number: Intl.NumberFormat; date: Intl.DateTimeFormat; }

const LocalizationContext = createContext<Localization | undefined>(undefined);

export function createTranslator(messages: Messages, fallback?: Messages) {
  return (key: string, values: Record<string, string | number> = {}) => {
    const template = messages[key] ?? fallback?.[key] ?? key;
    return template.replace(/\{(\w+)\}/g, (_, name: string) => String(values[name] ?? `{${name}}`));
  };
}

export function LocalizationProvider({ definition, fallbackMessages, children }: PropsWithChildren<{ definition: LocaleDefinition; fallbackMessages?: Messages }>) {
  const value = useMemo<Localization>(() => ({
    locale: definition.locale,
    t: createTranslator(definition.messages, fallbackMessages),
    number: new Intl.NumberFormat(definition.locale),
    date: new Intl.DateTimeFormat(definition.locale)
  }), [definition, fallbackMessages]);
  return <LocalizationContext.Provider value={value}>{children}</LocalizationContext.Provider>;
}

export function useLocalization() {
  const value = useContext(LocalizationContext);
  if (!value) throw new Error("useLocalization must be used inside a LocalizationProvider");
  return value;
}
