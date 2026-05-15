-- Load template for ZIP geographies used by shared radius search.
-- Expected source fields:
--   zip_code (text), city (text), state (text), latitude (numeric), longitude (numeric)
--
-- Example source options:
-- - a staging table populated by ETL
-- - COPY from a CSV into a temp/staging table

BEGIN;

CREATE TABLE IF NOT EXISTS "ZipCodeGeographies"
(
    "ZipCode" character varying(10) NOT NULL PRIMARY KEY,
    "City" character varying(100),
    "State" character varying(2),
    "Latitude" numeric(9,6) NOT NULL,
    "Longitude" numeric(9,6) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS "IX_ZipCodeGeographies_IsActive"
    ON "ZipCodeGeographies" ("IsActive");

CREATE INDEX IF NOT EXISTS "IX_ZipCodeGeographies_State_City"
    ON "ZipCodeGeographies" ("State", "City");

-- Example staging table.
CREATE TEMP TABLE zip_geo_stage
(
    zip_code text,
    city text,
    state text,
    latitude numeric,
    longitude numeric
);

-- Example CSV load (uncomment and adjust path on server):
-- COPY zip_geo_stage (zip_code, city, state, latitude, longitude)
-- FROM '/path/to/zip_geographies.csv'
-- WITH (FORMAT csv, HEADER true);

INSERT INTO "ZipCodeGeographies" ("ZipCode", "City", "State", "Latitude", "Longitude", "IsActive")
SELECT
    SUBSTRING(REGEXP_REPLACE(COALESCE(zip_code, ''), '[^0-9]', '', 'g') FROM 1 FOR 5) AS "ZipCode",
    NULLIF(BTRIM(city), '') AS "City",
    UPPER(NULLIF(BTRIM(state), '')) AS "State",
    ROUND(latitude::numeric, 6) AS "Latitude",
    ROUND(longitude::numeric, 6) AS "Longitude",
    TRUE AS "IsActive"
FROM zip_geo_stage
WHERE
    LENGTH(SUBSTRING(REGEXP_REPLACE(COALESCE(zip_code, ''), '[^0-9]', '', 'g') FROM 1 FOR 5)) = 5
    AND latitude IS NOT NULL
    AND longitude IS NOT NULL
ON CONFLICT ("ZipCode") DO UPDATE
SET
    "City" = EXCLUDED."City",
    "State" = EXCLUDED."State",
    "Latitude" = EXCLUDED."Latitude",
    "Longitude" = EXCLUDED."Longitude",
    "IsActive" = TRUE;

COMMIT;
