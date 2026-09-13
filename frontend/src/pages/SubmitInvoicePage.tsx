import {
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";

import {
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router-dom";

import {
  api,
  getApiErrorMessage,
} from "../api/client";

import {
  getMyPoGrns,
  getMyReceiptLines,
  resubmitInvoice,
  saveDraft,
  submitInvoice,
} from "../api/portal";

import type {
  OraclePoGrn,
  OracleReceiptLine,
  PortalInvoice,
} from "../types";

// ============================================================
// SAFE IDEMPOTENCY KEY
// ============================================================

function createIdempotencyKey() {
  if (
    typeof crypto !== "undefined" &&
    typeof crypto.randomUUID === "function"
  ) {
    return crypto.randomUUID();
  }

  return `${Date.now()}-${Math.random()
    .toString(36)
    .slice(2)}-${Math.random()
    .toString(36)
    .slice(2)}`;
}

// ============================================================
// UPLOAD ICON
// ============================================================

function UploadCloudIcon() {
  return (
    <svg
      className="upload-cloud-icon"
      viewBox="0 0 64 48"
      aria-hidden="true"
      focusable="false"
    >
      <path
        className="upload-cloud-fill"
        d="M20 42h28c8.8 0 16-6.8 16-15.2S57.3 12 49 11.7C45.9 4.8 39 0 31 0 20.4 0 11.7 8.3 11.1 18.7 4.7 20.4 0 26 0 32.7 0 37.9 4.4 42 10 42h10Z"
      />

      <path
        className="upload-cloud-arrow"
        d="M32 35V15m0 0-8 8m8-8 8 8"
      />
    </svg>
  );
}

// ============================================================
// UPLOAD DROPZONE
// ============================================================

type ExistingDocument =
  NonNullable<PortalInvoice["existingDocuments"]>[number];

type UploadDropZoneProps = {
  title: string;
  hint: string;
  files: File[];
  onFilesChange: (
    files: File[]
  ) => void;
  multiple?: boolean;
  existingFiles?: ExistingDocument[];
  onRemoveExisting?: (document: ExistingDocument) => void;
};

function UploadDropZone({
  title,
  hint,
  files,
  onFilesChange,
  multiple = true,
  existingFiles = [],
  onRemoveExisting,
}: UploadDropZoneProps) {
  const [
    dragging,
    setDragging,
  ] = useState(false);

  const [
    fileError,
    setFileError,
  ] = useState("");

  function acceptFiles(
    candidates:
      | FileList
      | File[]
      | null
      | undefined
  ) {
    if (
      !candidates ||
      candidates.length === 0
    ) {
      return;
    }

    setFileError("");

    const incoming =
      Array.from(
        candidates
      );

    const validFiles: File[] =
      [];

    const errors: string[] =
      [];

    const maxFileSize =
      1024 * 1024;

    for (
      const candidate of incoming
    ) {
      const fileName =
        candidate.name
          .toLowerCase();

      const allowed =
        candidate.type ===
          "application/pdf" ||
        candidate.type ===
          "image/png" ||
        candidate.type ===
          "image/jpeg" ||
        fileName.endsWith(
          ".pdf"
        ) ||
        fileName.endsWith(
          ".png"
        ) ||
        fileName.endsWith(
          ".jpg"
        ) ||
        fileName.endsWith(
          ".jpeg"
        );

      if (!allowed) {
        errors.push(
          `${candidate.name}: only PDF, PNG and JPG files are allowed.`
        );

        continue;
      }

      if (
        candidate.size <= 0 ||
        candidate.size >
          maxFileSize
      ) {
        errors.push(
          `${candidate.name}: file size must not exceed 1 MB.`
        );

        continue;
      }

      const duplicate =
        [
          ...files,
          ...validFiles,
        ].some(
          (existing) =>
            existing.name ===
              candidate.name &&
            existing.size ===
              candidate.size &&
            existing.lastModified ===
              candidate.lastModified
        );

      if (!duplicate) {
        validFiles.push(
          candidate
        );
      }
    }

    if (
      validFiles.length > 0
    ) {
      if (multiple) {
        onFilesChange([
          ...files,
          ...validFiles,
        ]);
      } else {
        onFilesChange([
          validFiles[0],
        ]);

        if (
          validFiles.length > 1
        ) {
          errors.push(
            "Only one Invoice Copy can be uploaded."
          );
        }
      }
    }

    if (
      errors.length > 0
    ) {
      setFileError(
        errors.join(" ")
      );
    }
  }

  function removeFile(
    index: number
  ) {
    onFilesChange(
      files.filter(
        (
          _,
          fileIndex
        ) =>
          fileIndex !== index
      )
    );
  }

  return (
    <div
      className={`invoice-upload-card ${
        dragging
          ? "dragging"
          : ""
      } ${
        files.length
          ? "has-file"
          : ""
      }`}
      onDragEnter={(
        event
      ) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragOver={(
        event
      ) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(
        event
      ) => {
        event.preventDefault();
        setDragging(false);
      }}
      onDrop={(event) => {
        event.preventDefault();
        setDragging(false);

        acceptFiles(
          event.dataTransfer
            .files
        );
      }}
    >
      <div className="invoice-upload-title">
        {title}
      </div>

      <label className="invoice-upload-dropzone">
        <UploadCloudIcon />

        <span className="invoice-upload-main-text">
          {multiple
            ? "Drag and drop files here"
            : "Drag and drop file here"}
        </span>

        <span className="invoice-upload-or">
          or
        </span>

        <span className="invoice-upload-select">
          {multiple
            ? "Select Files"
            : "Select File"}
        </span>

        <span className="invoice-upload-hint">
          {hint}
        </span>

        <input
          type="file"
          accept=".pdf,.png,.jpg,.jpeg"
          multiple={multiple}
          onChange={(
            event
          ) => {
            acceptFiles(
              event.target
                .files
            );

            event.currentTarget.value =
              "";
          }}
        />
      </label>

      {fileError && (
        <div className="invoice-upload-error">
          {fileError}
        </div>
      )}

      {files.length > 0 && (
        <div className="invoice-upload-selected-list">
          {files.map(
            (
              file,
              index
            ) => (
              <div
                className="invoice-upload-selected"
                key={`${file.name}-${file.size}-${file.lastModified}-${index}`}
              >
                <span
                  title={
                    file.name
                  }
                >
                  {file.name}
                </span>

                <button
                  type="button"
                  onClick={() =>
                    removeFile(
                      index
                    )
                  }
                >
                  Remove
                </button>
              </div>
            )
          )}
        </div>
      )}

      {existingFiles.length > 0 && (
        <div className="invoice-upload-selected-list">
          {existingFiles.map((document) => (
            <div
              className="invoice-upload-selected"
              key={document.id}
            >
              <span title={document.originalFileName}>
                {document.originalFileName}
              </span>

              {onRemoveExisting && (
                <button
                  type="button"
                  onClick={() => onRemoveExisting(document)}
                >
                  Remove
                </button>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ============================================================
// QC
// ============================================================

const pendingQc = (
  status?: string | null
) =>
  [
    "PENDING",
    "PENDING QC",
    "PENDING_QC",
    "AWAITING INSPECTION",
    "NOT RECEIVED",
  ].includes(
    (
      status || ""
    ).toUpperCase()
  );

// ============================================================
// PAGE
// ============================================================

export function SubmitInvoicePage() {
  const navigate =
    useNavigate();

  const { id } =
    useParams();

  const [searchParams] =
    useSearchParams();

  const isResubmit =
    Boolean(id);

  const [
    rows,
    setRows,
  ] =
    useState<
      OraclePoGrn[]
    >([]);

  const [
    receiptLines,
    setReceiptLines,
  ] =
    useState<
      OracleReceiptLine[]
    >([]);

  const [
    selectedReceiptTransactionIds,
    setSelectedReceiptTransactionIds,
  ] =
    useState<number[]>(
      []
    );

  const [
    receiptLinesLoading,
    setReceiptLinesLoading,
  ] =
    useState(false);

  // ============================================================
  // MULTI PO
  // ============================================================

  const [
    selectedPoNumbers,
    setSelectedPoNumbers,
  ] =
    useState<string[]>(
      []
    );

  const [
    poSearch,
    setPoSearch,
  ] =
    useState("");

  const [
    poDropdownOpen,
    setPoDropdownOpen,
  ] =
    useState(false);

  const poDropdownRef =
    useRef<
      HTMLDivElement | null
    >(null);

  const submissionIdempotencyKey =
    useRef<string>(
      createIdempotencyKey()
    );

  // ============================================================
  // GRN / INVOICE
  // ============================================================

  const [
    selected,
    setSelected,
  ] =
    useState<string[]>(
      []
    );

  const [
    invoiceNumber,
    setInvoiceNumber,
  ] =
    useState("");

  const [
    invoiceDate,
    setInvoiceDate,
  ] =
    useState("");

  const [
    invoiceAmount,
    setInvoiceAmount,
  ] =
    useState("");

  const [
    description,
    setDescription,
  ] =
    useState("");

  const [
    resubmitStatus,
    setResubmitStatus,
  ] =
    useState("");

  const [
    lastSuccessfulStep,
    setLastSuccessfulStep,
  ] =
    useState(
      "Submitted"
    );

  const [
    invoiceType,
    setInvoiceType,
  ] =
    useState<
      "GOODS" |
      "SERVICE"
    >("GOODS");

  const [
    invoiceFiles,
    setInvoiceFiles,
  ] =
    useState<File[]>(
      []
    );

  const [
    dcFiles,
    setDcFiles,
  ] =
    useState<File[]>(
      []
    );

  const [
    existingDocuments,
    setExistingDocuments,
  ] = useState<
    NonNullable<PortalInvoice["existingDocuments"]>
  >([]);

  const [
    removedExistingDocumentIds,
    setRemovedExistingDocumentIds,
  ] = useState<string[]>([]);

  const [
    error,
    setError,
  ] =
    useState("");

  const [
    busy,
    setBusy,
  ] =
    useState(false);

  // ============================================================
  // AUTO CALCULATE INVOICE AMOUNT
  // ============================================================

  const calculatedInvoiceAmount =
    useMemo(
      () =>
        receiptLines
          .filter(
            (
              line
            ) =>
              selectedReceiptTransactionIds.includes(
                line.rcvTransactionId
              )
          )
          .reduce(
            (
              total,
              line
            ) =>
              total +
              (
                Number(
                  line.availableQuantity ||
                    0
                ) *
                Number(
                  line.unitPrice ||
                    0
                )
              ),
            0
          ),
      [
        receiptLines,
        selectedReceiptTransactionIds,
      ]
    );

  useEffect(
    () => {
      if (
        selectedReceiptTransactionIds.length ===
        0
      ) {
        /*
         * For normal/new invoice, no selected receipt line means
         * no invoice amount.
         *
         * For resubmit, keep the previously loaded amount until
         * receipt lines have actually been selected/loaded.
         */
        if (!isResubmit) {
          setInvoiceAmount("");
        }

        return;
      }

      setInvoiceAmount(
        calculatedInvoiceAmount.toFixed(
          2
        )
      );
    },
    [
      calculatedInvoiceAmount,
      selectedReceiptTransactionIds.length,
      isResubmit,
    ]
  );

  // ============================================================
  // LOAD
  // ============================================================

  useEffect(() => {
    getMyPoGrns()
      .then(setRows)
      .catch(
        (err) =>
          setError(
            getApiErrorMessage(
              err
            )
          )
      );

    if (!id) {
      return;
    }

    api
      .get<PortalInvoice>(
        `/invoices/${id}`
      )
      .then(
        ({ data }) => {
          const currentStatus =
            (
              data.status ||
              ""
            ).toUpperCase();

          const currentIntegration =
            (
              data.integrationStatus ||
              ""
            ).toUpperCase();

          setResubmitStatus(
            currentStatus ===
              "RETURNED"
              ? "Returned for Correction"
              : "Action Required"
          );

          setLastSuccessfulStep(
            currentStatus ===
              "SENT_TO_ORACLE" ||
            currentIntegration ===
              "SUCCESS"
              ? "Sent to Oracle"
              : [
                  "INTEGRATION_FAILED",
                  "FAILED",
                  "ORACLE_REJECTED",
                ].includes(
                  currentStatus
                )
                ? "Not Sent to Oracle"
                : "Submitted"
          );

          const currentPoNumbers =
            data.poNumbers &&
            data.poNumbers.length
              ? data.poNumbers
              : (
                  data.poNumber ||
                  ""
                )
                  .split(",")
                  .map(
                    (x) =>
                      x.trim()
                  )
                  .filter(
                    Boolean
                  );

          setSelectedPoNumbers(
            currentPoNumbers
          );

          setSelected(
            data.grnNumbers ||
              []
          );

          setSelectedReceiptTransactionIds(
            data.rcvTransactionIds ||
              []
          );

          setExistingDocuments(
            data.existingDocuments ||
              []
          );

          setInvoiceNumber(
            data.invoiceNumber ||
              ""
          );

          setInvoiceDate(
            data.invoiceDate ||
              ""
          );

          setInvoiceAmount(
            String(
              data.invoiceAmount ||
                ""
            )
          );

          setDescription(
            data.description ||
              ""
          );

          setInvoiceType(
            (
              data.invoiceType ||
              "GOODS"
            ) as
              | "GOODS"
              | "SERVICE"
          );
        }
      )
      .catch(
        (err) =>
          setError(
            getApiErrorMessage(
              err
            )
          )
      );
  }, [id]);

  useEffect(() => {
    if (
      id ||
      !searchParams.get(
        "prefill"
      ) ||
      rows.length === 0
    ) {
      return;
    }

    const poNumber =
      searchParams.get(
        "po"
      );

    const grnNumbers =
      searchParams.getAll(
        "grn"
      );

    if (
      !poNumber ||
      !rows.some(
        (
          row
        ) =>
          row.poNumber ===
          poNumber
      )
    ) {
      return;
    }

    setSelectedPoNumbers(
      [
        poNumber,
      ]
    );

    setSelected(
      grnNumbers
    );

    /*
     * Do not calculate Invoice Amount here anymore.
     *
     * The exact amount will be calculated from the actual
     * selected Oracle receipt lines after they are loaded.
     */
  }, [
    id,
    rows,
    searchParams,
  ]);

  // ============================================================
  // CLOSE PO DROPDOWN OUTSIDE
  // ============================================================

  useEffect(() => {
    function handleOutsideClick(
      event: MouseEvent
    ) {
      if (
        poDropdownRef.current &&
        !poDropdownRef.current.contains(
          event.target as Node
        )
      ) {
        setPoDropdownOpen(
          false
        );

        setPoSearch("");
      }
    }

    document.addEventListener(
      "mousedown",
      handleOutsideClick
    );

    return () => {
      document.removeEventListener(
        "mousedown",
        handleOutsideClick
      );
    };
  }, []);

  // ============================================================
  // PO OPTIONS
  // ============================================================

  const purchaseOrders =
    useMemo(
      () =>
        [
          ...new Set(
            rows
              .filter(
                (
                  row
                ) =>
                  Boolean(
                    row.grnNumber
                  )
                  &&
                  !pendingQc(
                    row.inspectionStatus
                  )
                  &&
                  Number(
                    row.quantityAvailableToInvoice ??
                    0
                  ) > 0
              )
              .map(
                (
                  row
                ) =>
                  row.poNumber
              )
              .filter(
                (
                  value
                ): value is string =>
                  Boolean(
                    value
                  )
              )
          ),
        ].sort(
          (
            a,
            b
          ) =>
            a.localeCompare(
              b,
              undefined,
              {
                numeric:
                  true,
              }
            )
        ),
      [
        rows,
      ]
    );

  const filteredPurchaseOrders =
    useMemo(
      () => {
        const query =
          poSearch
            .trim()
            .toLowerCase();

        if (!query) {
          return purchaseOrders;
        }

        return purchaseOrders.filter(
          (
            number
          ) =>
            number
              .toLowerCase()
              .includes(
                query
              )
        );
      },
      [
        purchaseOrders,
        poSearch,
      ]
    );

  function togglePo(
    number: string
  ) {
    setSelectedPoNumbers(
      (
        current
      ) => {
        const exists =
          current.includes(
            number
          );

        const next =
          exists
            ? current.filter(
                (
                  value
                ) =>
                  value !==
                  number
              )
            : [
                ...current,
                number,
              ];

        if (exists) {
          const grnsForRemovedPo =
            new Set(
              rows
                .filter(
                  (
                    row
                  ) =>
                    row.poNumber ===
                      number &&
                    row.grnNumber
                )
                .map(
                  (
                    row
                  ) =>
                    row.grnNumber!
                )
            );

          setSelected(
            (
              currentGrns
            ) =>
              currentGrns.filter(
                (
                  grn
                ) =>
                  !grnsForRemovedPo.has(
                    grn
                  )
              )
          );

          const transactionIdsForRemovedPo =
            new Set(
              receiptLines
                .filter(
                  (
                    line
                  ) =>
                    line.poNumber ===
                    number
                )
                .map(
                  (
                    line
                  ) =>
                    line.rcvTransactionId
                )
            );

          setSelectedReceiptTransactionIds(
            (
              currentIds
            ) =>
              currentIds.filter(
                (
                  transactionId
                ) =>
                  !transactionIdsForRemovedPo.has(
                    transactionId
                  )
              )
          );
        }

        return next;
      }
    );
  }

  function removePo(
    number: string
  ) {
    const grnsForRemovedPo =
      new Set(
        rows
          .filter(
            (
              row
            ) =>
              row.poNumber ===
                number &&
              row.grnNumber
          )
          .map(
            (
              row
            ) =>
              row.grnNumber!
          )
      );

    const transactionIdsForRemovedPo =
      new Set(
        receiptLines
          .filter(
            (
              line
            ) =>
              line.poNumber ===
              number
          )
          .map(
            (
              line
            ) =>
              line.rcvTransactionId
          )
      );

    setSelectedPoNumbers(
      (
        current
      ) =>
        current.filter(
          (
            value
          ) =>
            value !==
            number
        )
    );

    setSelected(
      (
        current
      ) =>
        current.filter(
          (
            grn
          ) =>
            !grnsForRemovedPo.has(
              grn
            )
        )
    );

    setSelectedReceiptTransactionIds(
      (
        current
      ) =>
        current.filter(
          (
            transactionId
          ) =>
            !transactionIdsForRemovedPo.has(
              transactionId
            )
        )
    );
  }

  // ============================================================
  // LOAD RECEIPT LINES
  // ============================================================

  useEffect(() => {
    let cancelled =
      false;

    async function loadReceiptLines() {
      if (
        selectedPoNumbers.length ===
        0
      ) {
        setReceiptLines(
          []
        );

        setSelectedReceiptTransactionIds(
          []
        );

        return;
      }

      setReceiptLinesLoading(
        true
      );

      try {
        const groups =
          await Promise.all(
            selectedPoNumbers.map(
              async (
                po
              ) => {
                const grns = [
                  ...new Set(
                    rows
                      .filter(
                        (
                          r
                        ) =>
                          r.poNumber ===
                            po &&
                          r.grnNumber &&
                          !pendingQc(
                            r.inspectionStatus
                          )
                      )
                      .map(
                        (
                          r
                        ) =>
                          r.grnNumber!
                      )
                  ),
                ];

                return getMyReceiptLines(
                  po,
                  grns
                );
              }
            )
          );

        if (
          !cancelled
        ) {
          const loadedLines =
            groups
              .flat()
              .filter(
                (
                  x
                ) =>
                  Number(
                    x.availableQuantity
                  ) > 0
              );

          setReceiptLines(
            loadedLines
          );

          if (
            searchParams.get(
              "prefill"
            )
          ) {
            setSelectedReceiptTransactionIds(
              loadedLines.map(
                (
                  line
                ) =>
                  line.rcvTransactionId
              )
            );

            const grns =
              [
                ...new Set(
                  loadedLines.map(
                    (
                      line
                    ) =>
                      line.grnNumber
                  )
                ),
              ];

            setSelected(
              grns
            );
          } else if (
            isResubmit
          ) {
            /*
             * During resubmit, retain only transaction IDs that
             * still exist in the freshly loaded Oracle receipt lines.
             */
            setSelectedReceiptTransactionIds(
              (
                current
              ) => {
                const availableIds =
                  new Set(
                    loadedLines.map(
                      (
                        line
                      ) =>
                        line.rcvTransactionId
                    )
                  );

                return current.filter(
                  (
                    id
                  ) =>
                    availableIds.has(
                      id
                    )
                );
              }
            );
          }
        }
      } catch (
        err
      ) {
        if (
          !cancelled
        ) {
          setError(
            getApiErrorMessage(
              err
            )
          );
        }
      } finally {
        if (
          !cancelled
        ) {
          setReceiptLinesLoading(
            false
          );
        }
      }
    }

    loadReceiptLines();

    return () => {
      cancelled =
        true;
    };
  }, [
    rows,
    selectedPoNumbers,
    searchParams,
    isResubmit,
  ]);

  // ============================================================
  // TOGGLE RECEIPT LINE
  // ============================================================

  function toggleReceiptLine(
    line:
      OracleReceiptLine
  ) {
    setSelectedReceiptTransactionIds(
      (
        current
      ) => {
        const next =
          current.includes(
            line.rcvTransactionId
          )
            ? current.filter(
                (
                  x
                ) =>
                  x !==
                  line.rcvTransactionId
              )
            : [
                ...current,
                line.rcvTransactionId,
              ];

        const grns = [
          ...new Set(
            receiptLines
              .filter(
                (
                  x
                ) =>
                  next.includes(
                    x.rcvTransactionId
                  )
              )
              .map(
                (
                  x
                ) =>
                  x.grnNumber
              )
          ),
        ];

        setSelected(
          grns
        );

        return next;
      }
    );
  }

  // ============================================================
  // AVAILABLE GRNS FOR ALL SELECTED POS
  // ============================================================

  const availableGrns =
    useMemo(
      () => {
        if (
          selectedPoNumbers.length ===
          0
        ) {
          return [];
        }

        return rows.filter(
          (
            row
          ) => {
            const availableQuantity =
              Number(
                row.quantityAvailableToInvoice ??
                  row.grnReceivedQuantity ??
                  0
              );

            return (
              selectedPoNumbers.includes(
                row.poNumber
              ) &&
              Boolean(
                row.grnNumber
              ) &&
              availableQuantity >
                0
            );
          }
        );
      },
      [
        rows,
        selectedPoNumbers,
      ]
    );

  function toggleGrn(
    grnNumber: string
  ) {
    setSelected(
      (
        current
      ) =>
        current.includes(
          grnNumber
        )
          ? current.filter(
              (
                value
              ) =>
                value !==
                grnNumber
            )
          : [
              ...current,
              grnNumber,
            ]
    );
  }

  // Keep these referenced to avoid noUnusedLocals errors
  void availableGrns;
  void toggleGrn;

  function removeExistingDocument(
    document: ExistingDocument
  ) {
    setExistingDocuments((current) =>
      current.filter((x) => x.id !== document.id)
    );

    setRemovedExistingDocumentIds((current) =>
      current.includes(document.id)
        ? current
        : [...current, document.id]
    );
  }

  // ============================================================
  // SAVE
  // ============================================================

  async function save(
    draft: boolean
  ) {
    setError("");

    if (
      isResubmit &&
      draft
    ) {
      setError(
        "Returned invoices must be resubmitted, not saved as a new draft."
      );

      return;
    }

    if (
      !draft &&
      selectedPoNumbers.length ===
        0
    ) {
      setError(
        "Select at least one Purchase Order."
      );

      return;
    }

    if (
      !draft &&
      selectedReceiptTransactionIds.length ===
        0
    ) {
      setError(
        "Select at least one available GRN receipt line."
      );

      return;
    }

    if (
      !draft &&
      selected.length ===
        0
    ) {
      setError(
        "Select at least one available GRN."
      );

      return;
    }

    if (
      !draft &&
      !invoiceNumber.trim()
    ) {
      setError(
        "Invoice Number is required."
      );

      return;
    }

    if (
      !draft &&
      !invoiceDate
    ) {
      setError(
        "Invoice Date is required."
      );

      return;
    }

    if (
      !draft &&
      Number(
        invoiceAmount
      ) <= 0
    ) {
      setError(
        "Invoice Amount must be greater than zero."
      );

      return;
    }

    if (
      !draft &&
      !isResubmit &&
      invoiceFiles.length ===
        0
    ) {
      setError(
        "Invoice Copy is required."
      );

      return;
    }

    if (
      invoiceFiles.length >
      1
    ) {
      setError(
        "Only one Invoice Copy can be uploaded."
      );

      return;
    }

    if (
      !draft &&
      invoiceType ===
        "GOODS" &&
      dcFiles.length ===
        0 &&
      !isResubmit
    ) {
      setError(
        "At least one Receipted Delivery Challan is mandatory for goods invoices."
      );

      return;
    }

    setBusy(
      true
    );

    try {
      const formData =
        new FormData();

      selectedPoNumbers.forEach(
        (
          poNumber
        ) => {
          formData.append(
            "poNumbers",
            poNumber
          );
        }
      );

      formData.append(
        "poNumber",
        selectedPoNumbers.join(
          ","
        )
      );

      selected.forEach(
        (
          grnNumber
        ) => {
          formData.append(
            "grnNumbers",
            grnNumber
          );
        }
      );

      selectedReceiptTransactionIds.forEach(
        (
          transactionId
        ) => {
          formData.append(
            "rcvTransactionIds",
            String(
              transactionId
            )
          );
        }
      );

      removedExistingDocumentIds.forEach((documentId) => {
        formData.append(
          "removeDocumentIds",
          documentId
        );
      });

      formData.append(
        "invoiceNumber",
        invoiceNumber.trim()
      );

      formData.append(
        "invoiceDate",
        invoiceDate
      );

      formData.append(
        "invoiceAmount",
        invoiceAmount
      );

      formData.append(
        "invoiceType",
        invoiceType
      );

      formData.append(
        "description",
        description.trim()
      );

      if (
        invoiceFiles[0]
      ) {
        formData.append(
          "invoiceFiles",
          invoiceFiles[0]
        );
      }

      dcFiles.forEach(
        (
          file
        ) => {
          formData.append(
            "deliveryChallanFiles",
            file
          );
        }
      );

      if (
        isResubmit &&
        id
      ) {
        await resubmitInvoice(
          id,
          formData,
          submissionIdempotencyKey.current
        );
      } else if (
        draft
      ) {
        await saveDraft(
          formData
        );
      } else {
        await submitInvoice(
          formData,
          submissionIdempotencyKey.current
        );
      }

      navigate(
        "/invoices"
      );
    } catch (
      err
    ) {
      setError(
        getApiErrorMessage(
          err
        )
      );
    } finally {
      setBusy(
        false
      );
    }
  }

  // ============================================================
  // RENDER
  // ============================================================

  return (
    <div className="page-card form-page">
      <div className="page-card-head">
        <div>
          {(isResubmit ||
            searchParams.get(
              "prefill"
            )) && (
            <button
              type="button"
              className="table-btn invoice-back-btn"
              onClick={() =>
                navigate(
                  -1
                )
              }
            >
              ← Back
            </button>
          )}

          <h2>
            {isResubmit
              ? "Edit & Resubmit Invoice"
              : "Submit Invoice"}
          </h2>

          <p>
            {isResubmit
              ? "Returned invoice history is preserved; this creates a new status-history event."
              : "Select one or more POs and eligible GRNs. Finance review remains in Oracle EBS."}
          </p>
        </div>

        <span className="status blue">
          {isResubmit
            ? "Returned → Resubmit"
            : "Vendor Submission"}
        </span>
      </div>

      {error && (
        <div className="form-error">
          {error}
        </div>
      )}

      {isResubmit && (
        <div className="success-note">
          <strong>
            Reason for Return / Issue
          </strong>

          <div>
            {searchParams.get("issue") ||
              "Invoice was returned by Finance. Please review and resubmit."}
          </div>

          {searchParams.get("oracleRequestId") && (
            <>
              <strong>Oracle Request ID</strong>
              <div>{searchParams.get("oracleRequestId")}</div>
            </>
          )}

          {searchParams.get("integrationStatus") && (
            <>
              <strong>Integration Status</strong>
              <div>{searchParams.get("integrationStatus")}</div>
            </>
          )}

          <strong>
            Current Status
          </strong>

          <div>
            {resubmitStatus ||
              "Returned for Correction"}
          </div>

          <strong>
            Last Successful Step
          </strong>

          <div>
            {lastSuccessfulStep}
          </div>
        </div>
      )}

      <div className="form-grid">
        <label>
          Purchase Order

          <div
            className="po-search-select po-multi-select"
            ref={
              poDropdownRef
            }
          >
            <div
              className="po-multi-control"
              onClick={() =>
                setPoDropdownOpen(
                  true
                )
              }
            >
              <div className="po-selected-chips">
                {selectedPoNumbers.map(
                  (
                    number
                  ) => (
                    <span
                      key={
                        number
                      }
                      className="po-selected-chip"
                    >
                      {
                        number
                      }

                      <button
                        type="button"
                        onClick={(
                          event
                        ) => {
                          event.stopPropagation();

                          removePo(
                            number
                          );
                        }}
                      >
                        ×
                      </button>
                    </span>
                  )
                )}

                <input
                  type="text"
                  value={
                    poSearch
                  }
                  placeholder={
                    selectedPoNumbers.length
                      ? "Search more POs..."
                      : "Search or select PO..."
                  }
                  onFocus={() =>
                    setPoDropdownOpen(
                      true
                    )
                  }
                  onChange={(
                    event
                  ) => {
                    setPoSearch(
                      event.target
                        .value
                    );

                    setPoDropdownOpen(
                      true
                    );
                  }}
                />
              </div>

              <button
                type="button"
                className="po-search-toggle"
                onClick={(
                  event
                ) => {
                  event.stopPropagation();

                  setPoDropdownOpen(
                    (
                      current
                    ) =>
                      !current
                  );
                }}
              >
                ▾
              </button>
            </div>

            {poDropdownOpen && (
              <div className="po-search-menu po-multi-menu">
                {filteredPurchaseOrders.length >
                0 ? (
                  filteredPurchaseOrders.map(
                    (
                      number
                    ) => {
                      const checked =
                        selectedPoNumbers.includes(
                          number
                        );

                      return (
                        <button
                          key={
                            number
                          }
                          type="button"
                          className={`po-search-option po-multi-option ${
                            checked
                              ? "selected"
                              : ""
                          }`}
                          onClick={() =>
                            togglePo(
                              number
                            )
                          }
                        >
                          <span
                            className={`po-option-checkbox ${
                              checked
                                ? "checked"
                                : ""
                            }`}
                          >
                            {checked
                              ? "✓"
                              : ""}
                          </span>

                          <span>
                            {
                              number
                            }
                          </span>
                        </button>
                      );
                    }
                  )
                ) : (
                  <div className="po-search-empty">
                    No Purchase Orders found.
                  </div>
                )}
              </div>
            )}
          </div>
        </label>

        <label>
          Invoice Type

          <select
            value={
              invoiceType
            }
            onChange={(
              event
            ) =>
              setInvoiceType(
                event.target
                  .value as
                  | "GOODS"
                  | "SERVICE"
              )
            }
          >
            <option value="GOODS">
              Goods
            </option>

            <option value="SERVICE">
              Service
            </option>
          </select>
        </label>

        <label>
          Invoice Number

          <input
            value={
              invoiceNumber
            }
            onChange={(
              event
            ) =>
              setInvoiceNumber(
                event.target
                  .value
              )
            }
          />
        </label>

        <label>
          Invoice Date

          <input
            type="date"
            value={
              invoiceDate
            }
            onClick={(
              event
            ) => {
              const input =
                event.currentTarget;

              if (
                typeof input.showPicker ===
                "function"
              ) {
                input.showPicker();
              }
            }}
            onChange={(
              event
            ) =>
              setInvoiceDate(
                event.target
                  .value
              )
            }
          />
        </label>

        <label>
          Invoice Amount

          <input
            type="number"
            min="0"
            step="0.01"
            value={
              invoiceAmount
            }
            onChange={(
              event
            ) =>
              setInvoiceAmount(
                event.target
                  .value
              )
            }
          />

          <small
            style={{
              display:
                "block",
              marginTop:
                "5px",
              color:
                "#667085",
              fontSize:
                "12px",
            }}
          >
            Auto-calculated from selected GRN lines. You may adjust it if required.
          </small>
        </label>

        <label>
          Description

          <textarea
            className="invoice-description"
            value={
              description
            }
            maxLength={
              500
            }
            onChange={(
              event
            ) =>
              setDescription(
                event.target
                  .value
              )
            }
            placeholder="Enter invoice description"
          />
        </label>
      </div>

      <h3>
        Available GRNs
      </h3>

      <div className="grn-select-list receipt-line-list">
        {receiptLinesLoading && (
          <div className="empty-state">
            Loading GRN lines...
          </div>
        )}

        {!receiptLinesLoading &&
          receiptLines.map(
            (
              line
            ) => (
              <label
                key={
                  line.rcvTransactionId
                }
                className="grn-choice receipt-line-choice"
              >
                <input
                  type="checkbox"
                  checked={
                    selectedReceiptTransactionIds.includes(
                      line.rcvTransactionId
                    )
                  }
                  onChange={() =>
                    toggleReceiptLine(
                      line
                    )
                  }
                />

                <div>
                  <b>
                    GRN{" "}
                    {
                      line.grnNumber
                    }{" "}
                    · Line{" "}
                    {
                      line.poLineNumber
                    }
                  </b>

                  <strong>
                    {line.itemDescription ||
                      "Item description unavailable"}
                  </strong>

                  <span>
                    PO:{" "}
                    {
                      line.poNumber
                    }{" "}
                    · Received:{" "}
                    {Number(
                      line.receivedQuantity
                    ).toLocaleString()}{" "}
                    · Available:{" "}
                    {Number(
                      line.availableQuantity
                    ).toLocaleString()}{" "}
                    · Unit Price:{" "}
                    {Number(
                      line.unitPrice
                    ).toLocaleString()}
                  </span>

                  <small>
                    Amount: PKR{" "}
                    {Number(
                      line.availableQuantity *
                        line.unitPrice
                    ).toLocaleString()}
                  </small>
                </div>

                <span className="status green">
                  Available
                </span>
              </label>
            )
          )}

        {!receiptLinesLoading &&
          selectedPoNumbers.length >
            0 &&
          receiptLines.length ===
            0 && (
            <div className="empty-state">
              No eligible GRN receipt lines are available for the selected POs.
            </div>
          )}
      </div>

      <div className="upload-grid invoice-upload-grid">
        <UploadDropZone
          title={`Invoice Copy ${
            isResubmit
              ? "(replace if needed)"
              : "*"
          }`}
          hint="Supported file types: PDF, PNG, JPG · Max 1 MB · 1 file only"
          files={
            invoiceFiles
          }
          onFilesChange={
            setInvoiceFiles
          }
          multiple={
            false
          }
          existingFiles={
            isResubmit
              ? existingDocuments.filter(
                  (document) =>
                    document.documentType.toUpperCase() === "INVOICE"
                )
              : []
          }
          onRemoveExisting={removeExistingDocument}
        />

        <UploadDropZone
          title={`Receipted Delivery Challan ${
            invoiceType ===
              "GOODS" &&
            !isResubmit
              ? "*"
              : "(replace if needed)"
          }`}
          hint="Supported file types: PDF, PNG, JPG · Max 1 MB per file · Multiple files allowed"
          files={
            dcFiles
          }
          onFilesChange={
            setDcFiles
          }
          multiple
          existingFiles={
            isResubmit
              ? existingDocuments.filter(
                  (document) =>
                    document.documentType.toUpperCase() === "DELIVERY_CHALLAN"
                )
              : []
          }
          onRemoveExisting={removeExistingDocument}
        />
      </div>

      <div className="form-actions">
        {!isResubmit && (
          <button
            type="button"
            className="secondary-btn"
            disabled={
              busy
            }
            onClick={() =>
              save(
                true
              )
            }
          >
            Save Draft
          </button>
        )}

        <button
          type="button"
          className="primary-btn submit-btn"
          disabled={
            busy
          }
          onClick={() =>
            save(
              false
            )
          }
        >
          {busy
            ? "Saving..."
            : isResubmit
              ? "Resubmit Invoice"
              : "Submit"}
        </button>
      </div>
    </div>
  );
}