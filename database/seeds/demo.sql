INSERT INTO categories (id, name, is_active) VALUES
('00000000-0000-4000-8000-000000000002', 'Cervejas', 1),
('00000000-0000-4000-8000-000000000008', 'Kits', 1)
ON DUPLICATE KEY UPDATE id = id;

INSERT INTO payment_methods (id, code, name, is_active) VALUES
('00000000-0000-4000-8000-000000000004', 'Cash', 'Dinheiro', 1),
('00000000-0000-4000-8000-000000000006', 'CardOnFulfillment', 'Cartão no atendimento', 1),
('00000000-0000-4000-8000-000000000007', 'PixOnFulfillment', 'Pix no atendimento', 1)
ON DUPLICATE KEY UPDATE id = id;

INSERT INTO products (id, category_id, sku, name, description, unit_label, price, image_url, is_available, is_active, version, created_at_utc, updated_at_utc) VALUES
('00000000-0000-4000-8000-000000000003', '00000000-0000-4000-8000-000000000002', 'VB-PILSEN-350', 'VanderBeer Pilsen 350 ml', 'Produto de demonstração.', 'Lata 350 ml', 10.00, NULL, 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
('00000000-0000-4000-8000-000000000009', '00000000-0000-4000-8000-000000000008', 'VB-KIT-3', 'Kit VanderBeer 3 unidades', 'Produto de demonstração.', 'Kit com 3 unidades', 29.90, NULL, 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE id = id;
