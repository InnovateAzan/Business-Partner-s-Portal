-- Read-only AP Invoice Workbench attachment diagnostics.
-- Confirm installed columns before changing this query for another EBS release.
SELECT table_name, column_name, data_type
  FROM all_tab_columns
 WHERE owner IN ('APPS', 'APPLSYS')
   AND table_name IN ('FND_ATTACHED_DOCUMENTS', 'FND_DOCUMENTS',
                      'FND_DOCUMENTS_TL', 'FND_LOBS')
 ORDER BY table_name, column_id;

-- Set these to a known manual/visible Workbench FILE attachment and a portal
-- attachment. Defaults compare the validated 8194782 portal row to a historic
-- AP Invoice Workbench attachment in this environment.
DEFINE manual_attached_document_id = 9103019
DEFINE portal_attached_document_id = 9583914

SELECT CASE WHEN fad.attached_document_id = &manual_attached_document_id
            THEN 'MANUAL_VISIBLE' ELSE 'PORTAL' END AS source,
       fad.attached_document_id,
       fad.document_id,
       fad.entity_name,
       fad.pk1_value,
       fad.pk2_value,
       fad.pk3_value,
       fad.pk4_value,
       fad.pk5_value,
       fad.category_id,
       fdc.name AS category_name,
       fdc.user_name AS category_user_name,
       fad.seq_num,
       fad.status,
       fad.automatically_added_flag,
       fad.column1,
       fad.app_source_version,
       fad.created_by AS attached_created_by,
       fad.creation_date AS attached_creation_date,
       fd.security_type,
       fd.security_id,
       fd.publish_flag,
       fd.datatype_id,
       fd.usage_type,
       fd.media_id,
       fd.file_name AS document_file_name,
       fd.created_by AS document_created_by,
       fd.creation_date AS document_creation_date,
       fdt.language,
       fdt.source_lang,
       fdt.file_name,
       fdt.description,
       fdt.media_id AS tl_media_id,
       fl.file_id,
       fl.file_name AS lob_file_name,
       fl.file_content_type,
       fl.upload_date,
       fl.program_name,
       fl.program_tag,
       fl.language AS lob_language,
       fl.oracle_charset,
       fl.file_format
  FROM fnd_attached_documents fad
  JOIN fnd_documents fd ON fd.document_id = fad.document_id
  JOIN fnd_documents_tl fdt ON fdt.document_id = fd.document_id
  JOIN fnd_document_categories_vl fdc ON fdc.category_id = fad.category_id
  LEFT JOIN fnd_lobs fl ON fl.file_id = fd.media_id
 WHERE fad.attached_document_id IN
       (&manual_attached_document_id, &portal_attached_document_id)
 ORDER BY fad.attached_document_id, fdt.language;

-- This is the decisive visibility test: the APXINWKB Forms query must expose
-- the generated attachment and its filename. It is stronger than raw-table
-- existence and intentionally does not require FND_DOCUMENTS_TL.FILE_NAME.
SELECT attached_document_id, document_id, entity_name, pk1_value, category_id,
       datatype_id, security_type, security_id, publish_flag, usage_type,
       file_name, media_id, function_name, function_type
  FROM fnd_attached_docs_form_vl
 WHERE attached_document_id IN (&manual_attached_document_id,
                                &portal_attached_document_id)
   AND function_name = 'APXINWKB'
   AND function_type = 'O'
 ORDER BY attached_document_id;

-- Proves whether a configured category is visible to the Payables Invoice
-- Workbench AP_INVOICES attachment block. Replace the bind values as needed.
SELECT faf.function_name,
       faf.function_type,
       faf.enabled_flag AS function_enabled,
       fab.block_name,
       fab.security_type,
       fabe.data_object_code,
       fabe.pk1_field,
       fabe.pk2_field,
       fabe.pk3_field,
       fabe.pk4_field,
       fabe.pk5_field,
       fabe.query_permission_type,
       fdcu.enabled_flag AS category_enabled,
       fdc.category_id,
       fdc.name AS category_name,
       fdc.user_name AS category_user_name,
       fdc.start_date_active,
       fdc.end_date_active
  FROM fnd_attachment_functions faf
  JOIN fnd_attachment_blocks fab
    ON fab.attachment_function_id = faf.attachment_function_id
  JOIN fnd_attachment_blk_entities fabe
    ON fabe.attachment_blk_id = fab.attachment_blk_id
  JOIN fnd_doc_category_usages fdcu
    ON fdcu.attachment_function_id = faf.attachment_function_id
  JOIN fnd_document_categories_vl fdc
    ON fdc.category_id = fdcu.category_id
 WHERE faf.function_name = NVL('&attachment_function', 'APXINWKB')
   AND faf.function_type = NVL('&attachment_function_type', 'O')
   AND fabe.data_object_code = 'AP_INVOICES'
 ORDER BY fdc.name, fab.block_name;

-- File datatype resolution for binary FND_LOBS payloads.  This is independent
-- of category assignment and is the value written to FND_DOCUMENTS.DATATYPE_ID.
SELECT datatype_id, name, start_date_active, end_date_active
  FROM fnd_document_datatypes
 WHERE UPPER(name) = 'FILE'
 ORDER BY datatype_id;

-- Exact installed package signatures. Run this in the same APPS database used
-- by the portal before changing any attachment API call.
SELECT package_name,
       object_name,
       overload,
       sequence,
       argument_name,
       position,
       data_type,
       in_out,
       defaulted
  FROM all_arguments
 WHERE owner IN ('APPS', 'APPLSYS')
   AND package_name IN ('FND_ATTACHED_DOCUMENTS_PKG', 'FND_DOCUMENTS_PKG')
   AND object_name IN ('INSERT_ROW', 'ADD_LANGUAGE')
 ORDER BY package_name, object_name, overload, sequence;
