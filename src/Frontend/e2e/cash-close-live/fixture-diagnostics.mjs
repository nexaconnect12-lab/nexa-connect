// Fixture diagnostics are allowlisted; child output can contain connection secrets.
export function fixtureFailure(action,error){
  const operation=['approve','late','revoke'].includes(action)?action:'unknown';
  if(error?.killed===true&&error?.signal==='SIGTERM')return `Fixture ${operation} timed out after 30000ms; sensitive diagnostics suppressed.`;
  const match=String(error?.stderr??'').match(/^Cash-close fixture failed at (options|connections|empty-check|platform|restaurant|assignments|stores|cash) \((PostgresException|InvalidOperationException|TimeoutException|NpgsqlException|ArgumentException), SQLSTATE=(none|[0-9A-Z]{5})\); sensitive details suppressed\.\r?$/m);
  return match?`Fixture ${operation} failed at ${match[1]} (${match[2]}, SQLSTATE=${match[3]}); sensitive diagnostics suppressed.`
    :`Fixture ${operation} failed; sensitive diagnostics suppressed.`;
}
