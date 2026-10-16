const prefix = "atendimento:pending-opening:";

async function payloadHash(clientId, description) {
  const bytes = new TextEncoder().encode(`${clientId}:${description}`);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
}

export async function getOrCreateAttemptKey(clientId, attendantId, description) {
  const storageKey = prefix + attendantId + ":" + clientId;
  const hash = await payloadHash(clientId, description);
  const saved = JSON.parse(sessionStorage.getItem(storageKey) || "null");
  if (saved?.hash === hash && typeof saved.key === "string")
    return saved.key;

  const key = crypto.randomUUID();
  sessionStorage.setItem(storageKey, JSON.stringify({ hash, key }));
  return key;
}

export function clearAttemptKey(clientId, attendantId, key) {
  const storageKey = prefix + attendantId + ":" + clientId;
  const saved = JSON.parse(sessionStorage.getItem(storageKey) || "null");
  if (saved?.key === key)
    sessionStorage.removeItem(storageKey);
}
