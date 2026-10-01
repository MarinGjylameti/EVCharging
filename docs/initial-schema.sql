CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE accounts (
    id uuid NOT NULL,
    email character varying(254) NOT NULL,
    normalized_email character varying(254) NOT NULL,
    password_hash character varying(500) NOT NULL,
    first_name character varying(80) NOT NULL,
    last_name character varying(80) NOT NULL,
    phone character varying(32),
    vehicle_make character varying(80),
    vehicle_model character varying(80),
    battery_type character varying(80),
    role character varying(20) NOT NULL,
    language character varying(2) NOT NULL,
    push_enabled boolean NOT NULL,
    is_active boolean NOT NULL,
    archived_at timestamp with time zone,
    version bigint NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_accounts" PRIMARY KEY (id),
    CONSTRAINT ck_account_language CHECK (language IN ('Sq', 'En')),
    CONSTRAINT ck_account_role CHECK (role IN ('Driver', 'Admin', 'Superadmin'))
);

CREATE TABLE audit_entries (
    id uuid NOT NULL,
    actor_id uuid NOT NULL,
    actor_role character varying(20) NOT NULL,
    action character varying(80) NOT NULL,
    entity_id uuid NOT NULL,
    changes_json text,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_audit_entries" PRIMARY KEY (id)
);

CREATE TABLE login_sessions (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(64) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_login_sessions" PRIMARY KEY (id),
    CONSTRAINT "FK_login_sessions_accounts_user_id" FOREIGN KEY (user_id) REFERENCES accounts (id) ON DELETE RESTRICT
);

CREATE TABLE notifications (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    source_event_id character varying(200) NOT NULL,
    kind character varying(40) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_notifications" PRIMARY KEY (id),
    CONSTRAINT "FK_notifications_accounts_user_id" FOREIGN KEY (user_id) REFERENCES accounts (id) ON DELETE RESTRICT
);

CREATE TABLE device_registrations (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    session_id uuid NOT NULL,
    token character varying(256) NOT NULL,
    platform character varying(10) NOT NULL,
    is_active boolean NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_device_registrations" PRIMARY KEY (id),
    CONSTRAINT "FK_device_registrations_accounts_user_id" FOREIGN KEY (user_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_device_registrations_login_sessions_session_id" FOREIGN KEY (session_id) REFERENCES login_sessions (id) ON DELETE RESTRICT
);

CREATE TABLE push_deliveries (
    id uuid NOT NULL,
    notification_id uuid NOT NULL,
    device_id uuid NOT NULL,
    attempts integer NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    completed_at timestamp with time zone,
    ticket_id character varying(128),
    failure_code character varying(80),
    CONSTRAINT "PK_push_deliveries" PRIMARY KEY (id),
    CONSTRAINT "FK_push_deliveries_device_registrations_device_id" FOREIGN KEY (device_id) REFERENCES device_registrations (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_push_deliveries_notifications_notification_id" FOREIGN KEY (notification_id) REFERENCES notifications (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_accounts_normalized_email" ON accounts (normalized_email);

CREATE INDEX "IX_audit_entries_actor_id_recorded_at" ON audit_entries (actor_id, recorded_at);

CREATE INDEX "IX_device_registrations_session_id" ON device_registrations (session_id);

CREATE UNIQUE INDEX "IX_device_registrations_token" ON device_registrations (token);

CREATE INDEX "IX_device_registrations_user_id" ON device_registrations (user_id);

CREATE UNIQUE INDEX "IX_login_sessions_token_hash" ON login_sessions (token_hash);

CREATE INDEX "IX_login_sessions_user_id_expires_at" ON login_sessions (user_id, expires_at);

CREATE INDEX "IX_notifications_user_id_created_at" ON notifications (user_id, created_at);

CREATE UNIQUE INDEX "IX_notifications_user_id_source_event_id_kind" ON notifications (user_id, source_event_id, kind);

CREATE INDEX "IX_push_deliveries_completed_at_next_attempt_at" ON push_deliveries (completed_at, next_attempt_at);

CREATE INDEX "IX_push_deliveries_device_id" ON push_deliveries (device_id);

CREATE UNIQUE INDEX "IX_push_deliveries_notification_id_device_id" ON push_deliveries (notification_id, device_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260928172603_InitialIdentityAndNotifications', '10.0.3');

COMMIT;

START TRANSACTION;
CREATE TABLE charging_stations (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(120) NOT NULL,
    city character varying(80) NOT NULL,
    address character varying(200) NOT NULL,
    latitude double precision NOT NULL,
    longitude double precision NOT NULL,
    connector_type character varying(40) NOT NULL,
    connector_count integer NOT NULL,
    power_kw numeric(10,2) NOT NULL,
    price_per_kwh numeric(10,4) NOT NULL,
    currency character varying(3) NOT NULL,
    status character varying(20) NOT NULL,
    is_archived boolean NOT NULL,
    is_demo boolean NOT NULL,
    CONSTRAINT "PK_charging_stations" PRIMARY KEY (id),
    CONSTRAINT ck_station_capacity CHECK (connector_count > 0 AND power_kw > 0 AND price_per_kwh >= 0),
    CONSTRAINT ck_station_coordinates CHECK (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)
);

CREATE TABLE charging_history (
    id uuid NOT NULL,
    driver_id uuid NOT NULL,
    station_id uuid NOT NULL,
    started_at timestamp with time zone NOT NULL,
    ended_at timestamp with time zone NOT NULL,
    energy_kwh numeric(12,3) NOT NULL,
    unit_price_snapshot numeric(10,4) NOT NULL,
    total_cost numeric(12,2) NOT NULL,
    currency character varying(3) NOT NULL,
    is_demo boolean NOT NULL,
    CONSTRAINT "PK_charging_history" PRIMARY KEY (id),
    CONSTRAINT ck_history_measurement CHECK (energy_kwh >= 0 AND unit_price_snapshot >= 0 AND total_cost >= 0 AND ended_at >= started_at),
    CONSTRAINT "FK_charging_history_accounts_driver_id" FOREIGN KEY (driver_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_charging_history_charging_stations_station_id" FOREIGN KEY (station_id) REFERENCES charging_stations (id) ON DELETE RESTRICT
);

CREATE INDEX "IX_charging_history_driver_id_started_at" ON charging_history (driver_id, started_at);

CREATE INDEX "IX_charging_history_station_id" ON charging_history (station_id);

CREATE INDEX "IX_charging_stations_city_is_archived" ON charging_stations (city, is_archived);

CREATE UNIQUE INDEX "IX_charging_stations_code" ON charging_stations (code);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260928175858_DriverMapAndHistory', '10.0.3');

COMMIT;

START TRANSACTION;
ALTER TABLE charging_stations ADD version bigint NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001083802_StationVersion', '10.0.3');

COMMIT;

START TRANSACTION;
ALTER TABLE charging_stations ADD forecast_available_at timestamp with time zone;

ALTER TABLE charging_stations ADD forecast_connector_id integer;

ALTER TABLE charging_stations ADD forecast_observed_at timestamp with time zone;

ALTER TABLE charging_stations ADD forecast_source character varying(20);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001094819_ChargingAvailabilityForecast', '10.0.3');

COMMIT;

