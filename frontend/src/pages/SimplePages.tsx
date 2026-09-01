import { useEffect, useState } from "react";
import { api } from "../api/client";

type DownloadRow = {
  id: string;
  invoiceNumber: string;
  documentType: string;
  originalFileName: string;
  uploadedAt: string;
};

export function DownloadsPage() {
  const [rows, setRows] = useState<DownloadRow[]>([]);

  useEffect(() => {
    api
      .get("/documents/my")
      .then((response) => {
        setRows(response.data || []);
      })
      .catch(() => {
        setRows([]);
      });
  }, []);

  async function download(
    id: string,
    name: string
  ) {
    const response = await api.get(
      `/documents/${id}`,
      {
        responseType: "blob",
      }
    );

    const url = URL.createObjectURL(
      response.data
    );

    const link =
      document.createElement("a");

    link.href = url;
    link.download = name;

    document.body.appendChild(link);
    link.click();
    link.remove();

    URL.revokeObjectURL(url);
  }

  return (
    <div className="page-card">
      <h2>Downloads</h2>

      <p>
        Invoice copies and Receipted Delivery
        Challans stored against your portal
        submissions.
      </p>

      <table className="data-table">
        <thead>
          <tr>
            <th>Invoice</th>
            <th>Document Type</th>
            <th>File</th>
            <th>Uploaded</th>
            <th>Action</th>
          </tr>
        </thead>

        <tbody>
          {rows.map((row) => (
            <tr key={row.id}>
              <td>{row.invoiceNumber}</td>

              <td>
                {row.documentType}
              </td>

              <td>
                {row.originalFileName}
              </td>

              <td>
                {new Date(
                  row.uploadedAt
                ).toLocaleString()}
              </td>

              <td>
                <button
                  type="button"
                  className="table-btn download-btn"
                  onClick={() =>
                    download(
                      row.id,
                      row.originalFileName
                    )
                  }
                >
                  Download
                </button>
              </td>
            </tr>
          ))}

          {!rows.length && (
            <tr>
              <td
                colSpan={5}
                className="empty"
              >
                No downloadable portal documents
                yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

export function SupportPage() {
  return (
    <div className="page-card">
      <h2>Support</h2>

      <p>
        For supplier master or registered-email
        issues, contact Supply Chain. For invoice
        integration errors, contact Integration
        Support.
      </p>

      <div className="support-grid">
        <div>
          <b>Portal Access</b>
          <span>Supply Chain / IT Admin</span>
        </div>

        <div>
          <b>Invoice Integration</b>
          <span>Integration Support</span>
        </div>

        <div>
          <b>Finance Review</b>
          <span>Finance / AP in Oracle EBS</span>
        </div>
      </div>
    </div>
  );
}