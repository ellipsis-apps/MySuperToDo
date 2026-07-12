// Trim input values on blur and before form submit to remove accidental leading/trailing whitespace.
// This applies to standard inputs (text, password, email, search, tel, url) and textareas.
(function () {
  'use strict';

  const selector = 'input[type=text], input[type=password], input[type=email], input[type=search], input[type=tel], input[type=url], textarea';

  function trimElement(el) {
    try {
      if (!el) return;
      if (typeof el.value === 'string') {
        const trimmed = el.value.trim();
        if (el.value !== trimmed) {
          el.value = trimmed;
          // trigger input event so Blazor sees the updated value
          const ev = new Event('input', { bubbles: true, cancelable: false });
          el.dispatchEvent(ev);
        }
      }
    } catch (e) {
      // ignore
      // console.debug('trimInputs: failed to trim element', e);
    }
  }

  function trimAllIn(root) {
    const rootEl = root || document;
    const els = rootEl.querySelectorAll(selector);
    els.forEach(trimElement);
  }

  // Trim on focusout (blur) for interactive UX
  document.addEventListener('focusout', function (e) {
    const tgt = e.target;
    if (!tgt) return;
    if (tgt.matches && tgt.matches(selector)) {
      trimElement(tgt);
    }
  }, true);

  // Trim before any form submit
  document.addEventListener('submit', function (e) {
    try {
      trimAllIn(e.target);
    } catch (err) {
      // console.debug('trimInputs: error trimming on submit', err);
    }
  }, true);

  // Expose utility for manual invocation
  window.trimAllInputs = function (root) { trimAllIn(root); };

})();
