/* WNTerm — chuỗi giao diện cho các script trang (vi | en). Ngôn ngữ lấy từ <html lang>. */
(function (global) {
  'use strict';
  var lang = (document.documentElement.lang || 'vi').slice(0, 2) === 'en' ? 'en' : 'vi';

  var D = {
    vi: {
      'crypto.unsupported': 'Trình duyệt của bạn không hỗ trợ mã hóa an toàn (cần HTTPS và trình duyệt hiện đại).',
      'crypto.unsupported_short': 'Trình duyệt không hỗ trợ mã hóa an toàn (cần HTTPS).',
      'common.show': 'Hiện', 'common.hide': 'Ẩn',
      'common.error': 'Có lỗi: %s',
      'device.web': 'Trình duyệt web',
      'reg.email_bad': 'Email không hợp lệ.',
      'reg.pw_short': 'Mật khẩu cần ít nhất 10 ký tự.',
      'reg.pw_mismatch': 'Hai lần nhập mật khẩu chưa khớp.',
      'reg.agree_req': 'Vui lòng xác nhận bạn hiểu về mã khôi phục.',
      'reg.tos_req': 'Vui lòng đồng ý với Điều khoản sử dụng.',
      'reg.working': 'Đang tạo khóa bảo mật…',
      'reg.failed': 'Không tạo được tài khoản, hãy thử lại.',
      'reg.crypto_err': 'Có lỗi khi mã hóa: %s',
      'reg.hint_short': 'Cần ít nhất 10 ký tự.',
      'reg.hint_weak': 'Còn yếu — thêm độ dài hoặc ký tự khác loại.',
      'reg.hint_ok': 'Khá ổn.',
      'reg.hint_strong': 'Mạnh.',
      'reg.copied': 'Đã sao chép ✓',
      'reg.copy_manual': 'Hãy bôi đen và copy',
      'reg.file_name': 'wnterm-ma-khoi-phuc.txt',
      'reg.file_head': 'WebNow Terminal (WNTerm) — MÃ KHÔI PHỤC TÀI KHOẢN',
      'reg.file_foot': 'Cất kỹ file này. Ai có mã này và biết email của bạn có thể đặt lại mật khẩu tài khoản.',
      'login.need': 'Nhập email và mật khẩu.',
      'login.working': 'Đang đăng nhập…',
      'login.noserver': 'Không kết nối được máy chủ.',
      'login.failed': 'Đăng nhập không thành công.',
      'login.bad': 'Email hoặc mật khẩu không đúng.',
      'rec.need_email': 'Nhập email.',
      'rec.code_bad': 'Mã khôi phục không đúng định dạng (32 ký tự chữ và số).',
      'rec.pw_short': 'Mật khẩu mới cần ít nhất 10 ký tự.',
      'rec.working': 'Đang xử lý…',
      'rec.failed': 'Không khôi phục được.',
      'rec.bad': 'Email hoặc mã khôi phục không đúng.',
      'rec.undecryptable': 'Mã khôi phục không giải mã được dữ liệu.',
      'rec.reset_failed': 'Không đặt lại được mật khẩu.',
      'rec.done': 'Đã đặt lại mật khẩu. Mọi thiết bị đang đăng nhập đã bị đăng xuất. <a href="/dang-nhap" style="text-decoration:underline">Đăng nhập ngay</a>.',
      'acc.hello': 'Xin chào, %s',
      'acc.vault_ver': 'Phiên bản %1$s · cập nhật %2$s',
      'acc.vault_none': 'Chưa có (sẽ xuất hiện khi bạn đồng bộ từ ứng dụng)',
      'acc.this_device': 'Thiết bị này',
      'acc.last_used': 'dùng lần cuối',
      'acc.signout': 'Đăng xuất',
      'acc.new_short': 'Mật khẩu mới cần ít nhất 10 ký tự.',
      'acc.new_mismatch': 'Hai lần nhập mật khẩu mới chưa khớp.',
      'acc.keys_failed': 'Không lấy được khóa.',
      'acc.old_bad': 'Mật khẩu hiện tại không đúng.',
      'acc.pw_failed': 'Không đổi được mật khẩu.',
      'acc.pw_done': 'Đã đổi mật khẩu. Các thiết bị khác đã bị đăng xuất.',
      'acc.del_confirm': 'Xóa vĩnh viễn tài khoản %s? Không thể hoàn tác.',
      'acc.del_failed': 'Không xóa được tài khoản.',
      'acc.del_bad': 'Mật khẩu không đúng.',
      'acc.del_done': 'Tài khoản đã được xóa.',
      'acc.load_failed': 'Không tải được: %s',
      'err.bad_response': 'Máy chủ phản hồi không hợp lệ.',
      'err.too_large': 'Dữ liệu gửi lên quá lớn.',
      'err.bad_json': 'Dữ liệu không hợp lệ.',
      'err.not_found': 'Không có đường dẫn này.',
      'err.server_error': 'Lỗi máy chủ, vui lòng thử lại sau.',
      'err.bad_email': 'Email không hợp lệ.',
      'err.closed': 'Hiện chưa mở đăng ký tài khoản mới.',
      'err.rate_limited': 'Bạn thao tác quá nhanh, hãy thử lại sau ít phút.',
      'err.bad_kdf': 'Tham số mã hóa không hợp lệ.',
      'err.email_taken': 'Email này đã được đăng ký.',
      'err.captcha': 'Vui lòng xác minh bạn không phải robot.',
      'err.tos_required': 'Vui lòng đồng ý với Điều khoản sử dụng.',
      'err.bad_field': 'Dữ liệu không hợp lệ.'
    },
    en: {
      'crypto.unsupported': 'Your browser does not support secure encryption (HTTPS and a modern browser are required).',
      'crypto.unsupported_short': 'Your browser does not support secure encryption (HTTPS required).',
      'common.show': 'Show', 'common.hide': 'Hide',
      'common.error': 'Error: %s',
      'device.web': 'Web browser',
      'reg.email_bad': 'Invalid email address.',
      'reg.pw_short': 'Password must be at least 10 characters.',
      'reg.pw_mismatch': 'The two passwords do not match.',
      'reg.agree_req': 'Please confirm that you understand the recovery code.',
      'reg.tos_req': 'Please accept the Terms of Use.',
      'reg.working': 'Generating security keys…',
      'reg.failed': 'Could not create the account, please try again.',
      'reg.crypto_err': 'Encryption error: %s',
      'reg.hint_short': 'At least 10 characters needed.',
      'reg.hint_weak': 'Weak — add length or other kinds of characters.',
      'reg.hint_ok': 'Decent.',
      'reg.hint_strong': 'Strong.',
      'reg.copied': 'Copied ✓',
      'reg.copy_manual': 'Select the code and copy it',
      'reg.file_name': 'wnterm-recovery-code.txt',
      'reg.file_head': 'WebNow Terminal (WNTerm) — ACCOUNT RECOVERY CODE',
      'reg.file_foot': 'Keep this file safe. Anyone with this code who knows your email can reset your account password.',
      'login.need': 'Enter your email and password.',
      'login.working': 'Logging in…',
      'login.noserver': 'Could not reach the server.',
      'login.failed': 'Login failed.',
      'login.bad': 'Incorrect email or password.',
      'rec.need_email': 'Enter your email.',
      'rec.code_bad': 'The recovery code is not in the right format (32 letters and digits).',
      'rec.pw_short': 'The new password must be at least 10 characters.',
      'rec.working': 'Working…',
      'rec.failed': 'Could not recover the account.',
      'rec.bad': 'Incorrect email or recovery code.',
      'rec.undecryptable': 'The recovery code could not decrypt your data.',
      'rec.reset_failed': 'Could not reset the password.',
      'rec.done': 'Password reset. All signed-in devices have been signed out. <a href="/dang-nhap" style="text-decoration:underline">Log in now</a>.',
      'acc.hello': 'Hello, %s',
      'acc.vault_ver': 'Version %1$s · updated %2$s',
      'acc.vault_none': 'None yet (appears once you sync from the app)',
      'acc.this_device': 'This device',
      'acc.last_used': 'last used',
      'acc.signout': 'Sign out',
      'acc.new_short': 'The new password must be at least 10 characters.',
      'acc.new_mismatch': 'The two new passwords do not match.',
      'acc.keys_failed': 'Could not fetch the keys.',
      'acc.old_bad': 'The current password is incorrect.',
      'acc.pw_failed': 'Could not change the password.',
      'acc.pw_done': 'Password changed. Other devices have been signed out.',
      'acc.del_confirm': 'Permanently delete the account %s? This cannot be undone.',
      'acc.del_failed': 'Could not delete the account.',
      'acc.del_bad': 'Incorrect password.',
      'acc.del_done': 'Your account has been deleted.',
      'acc.load_failed': 'Failed to load: %s',
      'err.bad_response': 'The server returned an invalid response.',
      'err.too_large': 'The data sent is too large.',
      'err.bad_json': 'Invalid data.',
      'err.not_found': 'No such path.',
      'err.server_error': 'Server error, please try again later.',
      'err.bad_email': 'Invalid email address.',
      'err.closed': 'New account registration is not open right now.',
      'err.rate_limited': 'Too many attempts, please try again in a few minutes.',
      'err.bad_kdf': 'Invalid encryption parameters.',
      'err.email_taken': 'This email is already registered.',
      'err.captcha': 'Please verify that you are not a robot.',
      'err.tos_required': 'Please accept the Terms of Use.',
      'err.bad_field': 'Invalid data.'
    }
  };

  function t(key) {
    var s = (D[lang] && D[lang][key]) || D.vi[key] || key;
    var args = Array.prototype.slice.call(arguments, 1);
    var i = 0;
    return s.replace(/%(?:(\d)\$)?s/g, function (m, n) { return args[n ? n - 1 : i++]; });
  }

  /**
   * Thông báo lỗi người dùng thấy, dựa trên mã lỗi của API (không dùng message tiếng Việt từ máy chủ).
   * fallbackKey: dùng khi mã lỗi không có bản dịch; ctxKey: dùng cho bad_credentials / bad_recovery (ngữ cảnh riêng).
   */
  function err(res, fallbackKey, ctxKey) {
    var c = res && res.error;
    if (c === 'bad_credentials' || c === 'bad_recovery') return t(ctxKey || fallbackKey);
    if (c && D.vi['err.' + c]) return t('err.' + c);
    if (c && /^bad_/.test(c)) return t('err.bad_field');
    return t(fallbackKey);
  }

  global.WNI18N = { lang: lang, t: t, err: err };
})(window);
