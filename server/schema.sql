-- WNTerm server — lược đồ MySQL (utf8mb4). Chạy bằng: php tools/migrate.php

CREATE TABLE IF NOT EXISTS users (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  email             VARCHAR(190)    NOT NULL,
  auth_hash         VARCHAR(255)    NOT NULL,          -- password_hash(authKey) — authKey sinh từ mật khẩu ở phía người dùng
  recovery_hash     VARCHAR(255)    NOT NULL,          -- password_hash(recoveryAuth)
  wrapped_pw        TEXT            NOT NULL,          -- khóa dữ liệu, bọc bằng khóa từ mật khẩu (server không giải được)
  wrapped_recovery  TEXT            NOT NULL,          -- khóa dữ liệu, bọc bằng mã khôi phục
  kdf_iterations    INT UNSIGNED    NOT NULL,
  vault_version     INT UNSIGNED    NOT NULL DEFAULT 0,
  vault_updated_at  DATETIME        NULL,
  created_at        DATETIME        NOT NULL,
  last_login_at     DATETIME        NULL,
  disabled          TINYINT(1)      NOT NULL DEFAULT 0,
  UNIQUE KEY uq_users_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sessions (
  id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  user_id      BIGINT UNSIGNED NOT NULL,
  token_hash   CHAR(64)        NOT NULL,
  device_name  VARCHAR(120)    NOT NULL DEFAULT '',
  ip           VARCHAR(64)     NOT NULL DEFAULT '',
  created_at   DATETIME        NOT NULL,
  last_used_at DATETIME        NOT NULL,
  expires_at   DATETIME        NOT NULL,
  UNIQUE KEY uq_sessions_token (token_hash),
  KEY ix_sessions_user (user_id),
  CONSTRAINT fk_sessions_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS vault_blobs (
  user_id    BIGINT UNSIGNED NOT NULL,
  version    INT UNSIGNED    NOT NULL,
  data       MEDIUMBLOB      NOT NULL,
  created_at DATETIME        NOT NULL,
  PRIMARY KEY (user_id, version),
  CONSTRAINT fk_blobs_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS rate_limits (
  k            VARCHAR(190) NOT NULL PRIMARY KEY,
  window_start INT UNSIGNED NOT NULL,
  hits         INT UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Lịch sử phiên bản vault (dùng cho mọi kiểu lưu trữ: db hoặc s3) — để liệt kê/khôi phục bản cũ và dọn dẹp.
CREATE TABLE IF NOT EXISTS vault_history (
  user_id    BIGINT UNSIGNED NOT NULL,
  version    INT UNSIGNED    NOT NULL,
  created_at DATETIME        NOT NULL,
  size       INT UNSIGNED    NOT NULL DEFAULT 0,
  device     VARCHAR(120)    NOT NULL DEFAULT '',
  PRIMARY KEY (user_id, version),
  CONSTRAINT fk_history_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Nạp lịch sử cho các phiên bản đã có từ trước (chạy lại nhiều lần vẫn an toàn).
INSERT IGNORE INTO vault_history (user_id, version, created_at, size, device)
  SELECT user_id, version, created_at, LENGTH(data), '' FROM vault_blobs;

-- Bằng chứng người dùng đã đồng ý Điều khoản sử dụng (phiên bản, thời điểm, IP lúc đăng ký).
CREATE TABLE IF NOT EXISTS tos_acceptances (
  user_id     BIGINT UNSIGNED NOT NULL,
  version     VARCHAR(20)     NOT NULL,
  ip          VARCHAR(64)     NOT NULL DEFAULT '',
  accepted_at DATETIME        NOT NULL,
  PRIMARY KEY (user_id, version),
  CONSTRAINT fk_tos_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
