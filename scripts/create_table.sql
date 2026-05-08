CREATE TABLE message_log (
    id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    tenant_id VARCHAR(50) NOT NULL,
    requestor VARCHAR(50) NOT NULL,
    channel VARCHAR(20) NOT NULL,
    web_menu_id int NULL,
    recipient VARCHAR(255) NOT NULL,
    template_code VARCHAR(100) NOT NULL,
    email_json NVARCHAR(MAX) NULL,
    data_json NVARCHAR(MAX) NOT NULL,
    attachment_path NVARCHAR(MAX) NULL,
    status VARCHAR(20) NOT NULL,
    retry_count INT NOT NULL DEFAULT 0,
    error_message NVARCHAR(MAX) NULL,
    next_retry_at DATETIME NULL,
    processing_at DATETIME NULL,
    created_at DATETIME NOT NULL,
    sent_at DATETIME NULL
);

CREATE INDEX IX_message_log_status_created_at ON message_log (status, created_at);
CREATE NONCLUSTERED INDEX IX_message_log_processing
ON message_log (status, next_retry_at, processing_at, created_at)
INCLUDE (id);