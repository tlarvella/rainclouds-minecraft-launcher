/**
 * GitHub 登录（含 2FA 动态码）并创建 Classic PAT。
 * 用途：GitHub 已禁用密码认证（git/API），必须用 token；而发货账号只提供
 * 用户名 / 密码 / 16 位 TOTP 种子，因此需要先登录网页再生成 PAT。
 *
 * 用法：
 *   GH_USER=xxx GH_PASS=xxx GH_TOTP_SEED=XXX PW_CORE=<playwright-core 路径> node gh_login.js
 */
const crypto = require('crypto');

const PW_CORE = process.env.PW_CORE;
const { chromium } = require(PW_CORE);

const USER = process.env.GH_USER;
const PASS = process.env.GH_PASS;
const SEED = process.env.GH_TOTP_SEED;

function base32Decode(str) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of str.toUpperCase().replace(/=+$/, '')) {
    const i = alphabet.indexOf(c);
    if (i < 0) throw new Error('非法 base32 字符: ' + c);
    bits += i.toString(2).padStart(5, '0');
  }
  const bytes = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}

function totp(secret) {
  const key = base32Decode(secret);
  const counter = Math.floor(Date.now() / 1000 / 30);
  const buf = Buffer.alloc(8);
  buf.writeUInt32BE(Math.floor(counter / 0x100000000), 0);
  buf.writeUInt32BE(counter & 0xffffffff, 4);
  const h = crypto.createHmac('sha1', key).update(buf).digest();
  const off = h[h.length - 1] & 0xf;
  const code = (h.readUInt32BE(off) & 0x7fffffff) % 1000000;
  return String(code).padStart(6, '0');
}

function log(...a) { console.log('[gh_login]', ...a); }

(async () => {
  log('启动 chromium (no-sandbox)...');
  // 使用系统已安装的 Chrome，避免 playwright 自带浏览器版本不匹配
  const executablePath = process.env.CHROME_PATH || '/opt/google/chrome/chrome';
  log('executablePath:', executablePath);
  const browser = await chromium.launch({
    executablePath,
    chromiumSandbox: false,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage'],
  });
  const ctx = await browser.newContext({
    viewport: { width: 1400, height: 900 },
    userAgent:
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36',
  });
  const page = await ctx.newPage();

  try {
    // ---------- 1. 登录 ----------
    log('打开登录页...');
    await page.goto('https://github.com/login', { waitUntil: 'domcontentloaded', timeout: 90000 });
    await page.waitForSelector('#login_field', { timeout: 60000 });
    await page.fill('#login_field', USER);
    await page.fill('#password', PASS);
    log('提交用户名密码...');
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => {}),
      page.click('input[type="submit"], button[type="submit"]'),
    ]);
    await page.waitForTimeout(3500);

    let url = page.url();
    log('登录后 URL:', url);
    // 读取页面文本与错误提示，定位登录失败原因
    const bodyText = await page.evaluate(() => (document.body.innerText || '').slice(0, 2000)).catch(() => '');
    log('页面文本: ' + bodyText.replace(/\s+/g, ' ').slice(0, 700));
    const errText = await page
      .$eval('.flash-error, .flash, #js-flash-container', (el) => el.innerText)
      .catch(() => null);
    if (errText) log('⚠️ 页面提示:', errText.trim().slice(0, 300));

    // ---------- 2. 2FA ----------
    if (url.includes('two-factor') || (await page.$('#otp'))) {
      const code = totp(SEED);
      log('检测到 2FA，使用动态码:', code);
      await page.fill('#otp', code);
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => {}),
        page.click('button[type="submit"], input[type="submit"]'),
      ]);
      await page.waitForTimeout(3000);
      url = page.url();
      log('2FA 后 URL:', url);
      await page.screenshot({ path: '/tmp/gh_after_2fa.png' });
    }

    // ---------- 3. 校验登录状态 ----------
    const who = await page
      .goto('https://github.com/settings/profile', { waitUntil: 'domcontentloaded', timeout: 60000 })
      .then(() => page.url())
      .catch((e) => 'ERR:' + e.message);
    log('设置页 URL:', who);
    if (who.includes('/login') || String(who).startsWith('ERR')) {
      log('❌ 未登录成功，请检查凭据或是否有额外验证（邮件/设备验证）');
      await page.screenshot({ path: '/tmp/gh_fail.png' });
      await browser.close();
      process.exit(2);
    }
    log('✅ 登录成功');

    // ---------- 4. 创建 Classic PAT ----------
    log('打开 PAT 创建页...');
    await page.goto('https://github.com/settings/tokens/new', { waitUntil: 'domcontentloaded', timeout: 60000 });
    await page.waitForTimeout(1500);

    const note = 'rcl-upload-' + Date.now();
    // Note 输入框
    const descSel = '#oauth_access_description, input[name="oauth_access[description]"]';
    if (await page.$(descSel)) {
      await page.fill(descSel, note);
      log('已填写 Note:', note);
    } else {
      log('⚠️ 未找到 Note 输入框');
    }

    // 勾选 repo 作用域（含 public_repo，足够建库/发版本）
    const checked = await page.evaluate(() => {
      const boxes = Array.from(document.querySelectorAll('input[type="checkbox"]'));
      const hit = boxes.filter((b) => {
        const v = (b.value || '').toLowerCase();
        const id = (b.id || '').toLowerCase();
        const name = (b.getAttribute('name') || '').toLowerCase();
        return v === 'repo' || id.includes('oauth_access_repo') || name.includes('repo');
      });
      hit.forEach((b) => { if (!b.checked) b.click(); });
      return hit.map((b) => b.value || b.id);
    });
    log('已勾选作用域:', JSON.stringify(checked));

    // 过期时间：选 90 天（如存在）
    const exp = await page.$('select[name="oauth_access[expires_at]"], #oauth_access_expires_at');
    if (exp) {
      try { await exp.selectOption({ label: '90 days' }); log('过期时间: 90 days'); } catch (e) { log('过期时间选择失败:', e.message); }
    }

    await page.screenshot({ path: '/tmp/gh_pat_form.png' });

    // 生成
    const genSel = 'button[type="submit"], input[type="submit"]';
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => {}),
      page.click(genSel),
    ]);
    await page.waitForTimeout(3000);
    await page.screenshot({ path: '/tmp/gh_pat_done.png' });
    log('提交后 URL:', page.url());

    // ---------- 5. 读取 token ----------
    // 生成的 token 形如 ghp_xxx，直接在页面文本里找
    const token = await page.evaluate(() => {
      const m = document.body.innerText.match(/gh[pousr]_[A-Za-z0-9]{20,}/);
      return m ? m[0] : null;
    });

    if (token) {
      log('✅ 已生成 PAT: ' + token.slice(0, 8) + '...');
      console.log('TOKEN=' + token);
    } else {
      log('⚠️ 页面上未直接找到 token，尝试从 code 元素读取');
      const alt = await page.evaluate(() => {
        const el = document.querySelector('#new-oauth-token, .color-fg-success code, pre code, code');
        return el ? el.textContent.trim() : null;
      });
      if (alt && /^gh[pousr]_/.test(alt)) {
        log('✅ 已从元素读取 PAT');
        console.log('TOKEN=' + alt);
      } else {
        log('❌ 未能读取 token（可能需人工确认或作用域不足）');
      }
    }
  } catch (e) {
    log('异常:', e.message);
    try { await page.screenshot({ path: '/tmp/gh_error.png' }); } catch (_) {}
  } finally {
    await browser.close();
  }
})();
