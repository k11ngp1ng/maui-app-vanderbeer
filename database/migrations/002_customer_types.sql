ALTER TABLE customers
    MODIFY cpf CHAR(11) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD COLUMN customer_type VARCHAR(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Individual' AFTER id,
    ADD COLUMN company_name VARCHAR(120) NULL AFTER full_name,
    ADD COLUMN cnpj CHAR(14) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER cpf,
    ADD CONSTRAINT uq_customers_cnpj UNIQUE (cnpj),
    ADD CONSTRAINT ck_customers_type CHECK (
        (customer_type = 'Individual' AND cpf IS NOT NULL AND cnpj IS NULL AND company_name IS NULL)
        OR (customer_type = 'Business' AND cpf IS NULL AND cnpj IS NOT NULL AND company_name IS NOT NULL)
    ),
    ADD CONSTRAINT ck_customers_cnpj CHECK (cnpj IS NULL OR cnpj REGEXP '^[0-9]{14}$'),
    ADD CONSTRAINT ck_customers_company_name CHECK (company_name IS NULL OR CHAR_LENGTH(TRIM(company_name)) BETWEEN 3 AND 120);
