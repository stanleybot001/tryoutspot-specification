START TRANSACTION;
ALTER TABLE "Users" ADD "SmsConsentAccepted" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "Users" ADD "SmsConsentAcceptedAt" timestamp with time zone;

ALTER TABLE "Users" ADD "SmsConsentSource" character varying(100);

ALTER TABLE "Users" ADD "SmsConsentText" character varying(1000);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260506033039_AddSmsConsentAudit', '9.0.15');

COMMIT;

