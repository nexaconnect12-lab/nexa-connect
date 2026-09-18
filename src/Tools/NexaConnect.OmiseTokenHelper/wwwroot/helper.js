'use strict';
const form = document.querySelector('#generate');
const button = document.querySelector('#submit');
const copy = document.querySelector('#copy');
const token = document.querySelector('#token');
const status = document.querySelector('#status');
form.addEventListener('submit', event => {
  event.preventDefault();
  token.value = '';
  copy.disabled = true;
  const key = document.querySelector('#public-key').value.trim();
  if (!/^pkey_test_[a-z0-9]{10,64}$/.test(key)) {
    status.textContent = 'Enter a test public key beginning with pkey_test_. Secret and live keys are not accepted.';
    return;
  }
  if (typeof Omise === 'undefined') {
    status.textContent = 'Omise.js did not load. Check your internet connection and access to cdn.omise.co.';
    return;
  }
  button.disabled = true;
  status.textContent = 'Generating…';
  Omise.setPublicKey(key);
  Omise.createToken('card', {
    name: 'Test Customer', number: '4242424242424242',
    expiration_month: 12, expiration_year: new Date().getFullYear() + 2, security_code: '123'
  }, (code, response) => {
    button.disabled = false;
    if (code === 200 && response && /^tokn_test_[a-z0-9]{10,64}$/.test(response.id)) {
      token.value = response.id;
      copy.disabled = false;
      status.textContent = 'Fresh token ready. Copy it and use it promptly.';
    } else {
      status.textContent = 'Token generation failed. Check that the public key belongs to your test account and card payments are enabled. No charge was created.';
    }
  });
});
copy.addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(token.value);
    status.textContent = 'Token copied. Paste it into the masked PowerShell prompt.';
  } catch {
    status.textContent = 'Clipboard access was denied. Allow clipboard access for this localhost page and try again.';
  }
});
