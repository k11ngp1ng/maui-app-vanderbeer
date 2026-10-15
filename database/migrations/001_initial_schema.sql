CREATE TABLE schema_migrations (
    version VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    checksum_sha256 BINARY(32) NOT NULL,
    applied_at_utc DATETIME(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE customers (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    full_name VARCHAR(120) NOT NULL,
    cpf CHAR(11) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    phone VARCHAR(11) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    email VARCHAR(254) NULL,
    version INT NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT uq_customers_cpf UNIQUE (cpf),
    CONSTRAINT ck_customers_version CHECK (version >= 1),
    CONSTRAINT ck_customers_name CHECK (CHAR_LENGTH(TRIM(full_name)) BETWEEN 3 AND 120),
    CONSTRAINT ck_customers_cpf CHECK (cpf REGEXP '^[0-9]{11}$'),
    CONSTRAINT ck_customers_phone CHECK (phone REGEXP '^[0-9]{10,11}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE customer_addresses (
    customer_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    postal_code CHAR(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    street VARCHAR(120) NOT NULL,
    number VARCHAR(20) NOT NULL,
    complement VARCHAR(80) NULL,
    district VARCHAR(80) NOT NULL,
    city VARCHAR(80) NOT NULL,
    state CHAR(2) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    reference VARCHAR(160) NULL,
    CONSTRAINT fk_customer_addresses_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_customer_addresses_postal_code CHECK (postal_code REGEXP '^[0-9]{8}$'),
    CONSTRAINT ck_customer_addresses_street CHECK (CHAR_LENGTH(TRIM(street)) BETWEEN 2 AND 120),
    CONSTRAINT ck_customer_addresses_number CHECK (CHAR_LENGTH(TRIM(number)) BETWEEN 1 AND 20),
    CONSTRAINT ck_customer_addresses_complement CHECK (complement IS NULL OR CHAR_LENGTH(TRIM(complement)) BETWEEN 1 AND 80),
    CONSTRAINT ck_customer_addresses_district CHECK (CHAR_LENGTH(TRIM(district)) BETWEEN 2 AND 80),
    CONSTRAINT ck_customer_addresses_city CHECK (CHAR_LENGTH(TRIM(city)) BETWEEN 2 AND 80),
    CONSTRAINT ck_customer_addresses_state CHECK (state IN ('AC','AL','AP','AM','BA','CE','DF','ES','GO','MA','MT','MS','MG','PA','PB','PR','PE','PI','RJ','RN','RS','RO','RR','SC','SP','SE','TO')),
    CONSTRAINT ck_customer_addresses_reference CHECK (reference IS NULL OR CHAR_LENGTH(TRIM(reference)) BETWEEN 1 AND 160)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE categories (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    name VARCHAR(80) NOT NULL,
    is_active TINYINT NOT NULL DEFAULT 1,
    CONSTRAINT uq_categories_name UNIQUE (name),
    CONSTRAINT ck_categories_name CHECK (CHAR_LENGTH(TRIM(name)) BETWEEN 2 AND 80),
    CONSTRAINT ck_categories_active CHECK (is_active IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE payment_methods (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    code VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(60) NOT NULL,
    is_active TINYINT NOT NULL DEFAULT 1,
    CONSTRAINT uq_payment_methods_code UNIQUE (code),
    CONSTRAINT ck_payment_methods_code CHECK (code IN ('Cash','CardOnFulfillment','PixOnFulfillment')),
    CONSTRAINT ck_payment_methods_name CHECK (CHAR_LENGTH(TRIM(name)) BETWEEN 2 AND 60),
    CONSTRAINT ck_payment_methods_active CHECK (is_active IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE products (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    category_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    sku VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(120) NOT NULL,
    description VARCHAR(2000) NULL,
    unit_label VARCHAR(40) NOT NULL,
    price DECIMAL(12,2) NOT NULL,
    image_url VARCHAR(1024) NULL,
    is_available TINYINT NOT NULL DEFAULT 1,
    is_active TINYINT NOT NULL DEFAULT 1,
    version INT NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT uq_products_sku UNIQUE (sku),
    CONSTRAINT fk_products_category FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_products_name CHECK (CHAR_LENGTH(TRIM(name)) BETWEEN 2 AND 120),
    CONSTRAINT ck_products_unit_label CHECK (CHAR_LENGTH(TRIM(unit_label)) BETWEEN 1 AND 40),
    CONSTRAINT ck_products_description CHECK (description IS NULL OR CHAR_LENGTH(TRIM(description)) BETWEEN 1 AND 2000),
    CONSTRAINT ck_products_price CHECK (price BETWEEN 0.01 AND 99999.99),
    CONSTRAINT ck_products_available CHECK (is_available IN (0,1)),
    CONSTRAINT ck_products_active CHECK (is_active IN (0,1)),
    CONSTRAINT ck_products_version CHECK (version >= 1),
    INDEX ix_products_active_category_name (is_active, category_id, name, id),
    INDEX ix_products_active_name (is_active, name, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE request_receipts (
    client_request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    operation VARCHAR(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_hash BINARY(32) NOT NULL,
    original_status SMALLINT NULL,
    response_json JSON NULL,
    resource_type VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL,
    resource_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    created_at_utc DATETIME(6) NOT NULL,
    completed_at_utc DATETIME(6) NULL,
    CONSTRAINT ck_request_receipts_status CHECK (original_status IS NULL OR original_status IN (200,201,400,404,409)),
    CONSTRAINT ck_request_receipts_completion CHECK (
        (original_status IS NULL AND response_json IS NULL AND completed_at_utc IS NULL)
        OR (original_status IS NOT NULL AND response_json IS NOT NULL AND completed_at_utc IS NOT NULL)
    ),
    CONSTRAINT ck_request_receipts_resource CHECK (
        (original_status IS NULL AND resource_type IS NULL AND resource_id IS NULL)
        OR (original_status IN (200,201) AND resource_type IN ('Customer','Product','Order') AND resource_id IS NOT NULL)
        OR (original_status IN (400,404,409) AND resource_type IS NULL AND resource_id IS NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE orders (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    order_number BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    customer_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    creation_request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    customer_name VARCHAR(120) NOT NULL,
    customer_phone VARCHAR(11) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    fulfillment_type VARCHAR(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    requested_date DATE NOT NULL,
    delivery_address_json JSON NULL,
    payment_method_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    payment_code VARCHAR(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    payment_name VARCHAR(60) NOT NULL,
    cash_tendered DECIMAL(12,2) NULL,
    change_due DECIMAL(12,2) NOT NULL DEFAULT 0,
    delivery_fee DECIMAL(12,2) NOT NULL DEFAULT 0,
    total DECIMAL(12,2) NOT NULL,
    notes VARCHAR(500) NULL,
    status VARCHAR(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    version INT NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT uq_orders_order_number UNIQUE (order_number),
    CONSTRAINT uq_orders_creation_request UNIQUE (creation_request_id),
    CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_orders_receipt FOREIGN KEY (creation_request_id) REFERENCES request_receipts(client_request_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_orders_payment_method FOREIGN KEY (payment_method_id) REFERENCES payment_methods(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_orders_customer_name CHECK (CHAR_LENGTH(TRIM(customer_name)) BETWEEN 3 AND 120),
    CONSTRAINT ck_orders_customer_phone CHECK (customer_phone REGEXP '^[0-9]{10,11}$'),
    CONSTRAINT ck_orders_fulfillment CHECK ((fulfillment_type = 'Delivery' AND delivery_address_json IS NOT NULL) OR (fulfillment_type = 'Pickup' AND delivery_address_json IS NULL)),
    CONSTRAINT ck_orders_cash CHECK ((payment_code = 'Cash' AND (cash_tendered IS NULL OR cash_tendered BETWEEN total AND 999999.99)) OR (payment_code <> 'Cash' AND cash_tendered IS NULL)),
    CONSTRAINT ck_orders_change CHECK (change_due = IFNULL(cash_tendered - total, 0)),
    CONSTRAINT ck_orders_delivery_fee CHECK (delivery_fee = 0),
    CONSTRAINT ck_orders_total CHECK (total BETWEEN 0.01 AND 999999.99),
    CONSTRAINT ck_orders_notes CHECK (notes IS NULL OR CHAR_LENGTH(TRIM(notes)) BETWEEN 1 AND 500),
    CONSTRAINT ck_orders_status CHECK (status IN ('Received','Confirmed','Preparing','OutForDelivery','Delivered','ReadyForPickup','Collected','Rejected','Cancelled')),
    CONSTRAINT ck_orders_status_fulfillment CHECK ((fulfillment_type = 'Delivery' AND status NOT IN ('ReadyForPickup','Collected')) OR (fulfillment_type = 'Pickup' AND status NOT IN ('OutForDelivery','Delivered'))),
    CONSTRAINT ck_orders_version CHECK (version >= 1),
    INDEX ix_orders_customer_created (customer_id, created_at_utc, id),
    INDEX ix_orders_status_created (status, created_at_utc, id),
    INDEX ix_orders_created (created_at_utc, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE order_items (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    order_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    product_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    product_name VARCHAR(120) NOT NULL,
    unit_label VARCHAR(40) NOT NULL,
    quantity SMALLINT NOT NULL,
    unit_price DECIMAL(12,2) NOT NULL,
    subtotal DECIMAL(12,2) NOT NULL,
    CONSTRAINT uq_order_items_product UNIQUE (order_id, product_id),
    CONSTRAINT fk_order_items_order FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_order_items_product FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_order_items_name CHECK (CHAR_LENGTH(TRIM(product_name)) BETWEEN 2 AND 120),
    CONSTRAINT ck_order_items_unit_label CHECK (CHAR_LENGTH(TRIM(unit_label)) BETWEEN 1 AND 40),
    CONSTRAINT ck_order_items_quantity CHECK (quantity BETWEEN 1 AND 999),
    CONSTRAINT ck_order_items_unit_price CHECK (unit_price BETWEEN 0.01 AND 99999.99),
    CONSTRAINT ck_order_items_subtotal CHECK (subtotal = quantity * unit_price AND subtotal BETWEEN 0.01 AND 999999.99)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE order_status_history (
    order_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    sequence INT NOT NULL,
    from_status VARCHAR(20) CHARACTER SET ascii COLLATE ascii_bin NULL,
    to_status VARCHAR(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    reason VARCHAR(300) NULL,
    occurred_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (order_id, sequence),
    CONSTRAINT fk_order_status_history_order FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_order_status_history_sequence CHECK (sequence >= 1),
    CONSTRAINT ck_order_status_history_from CHECK (from_status IS NULL OR from_status IN ('Received','Confirmed','Preparing','OutForDelivery','Delivered','ReadyForPickup','Collected','Rejected','Cancelled')),
    CONSTRAINT ck_order_status_history_to CHECK (to_status IN ('Received','Confirmed','Preparing','OutForDelivery','Delivered','ReadyForPickup','Collected','Rejected','Cancelled')),
    CONSTRAINT ck_order_status_history_initial CHECK ((sequence = 1 AND from_status IS NULL AND to_status = 'Received') OR (sequence > 1 AND from_status IS NOT NULL)),
    CONSTRAINT ck_order_status_history_reason CHECK ((to_status IN ('Rejected','Cancelled') AND reason IS NOT NULL AND CHAR_LENGTH(TRIM(reason)) BETWEEN 3 AND 300) OR (to_status NOT IN ('Rejected','Cancelled') AND reason IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
