namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface ILocalSalesContractService
{
    Task<LocalSalesContractTermsDto> GetTermsAsync(CancellationToken ct = default);
    Task<LocalSalesContractTermsDto> UpdateTermsAsync(UpsertLocalSalesContractTermsRequest request, Guid actorId, CancellationToken ct = default);

    Task<List<LocalSalesContractDocumentDto>> ListIssuedAsync(Guid? customerId, Guid? contractId, CancellationToken ct = default);
    Task<LocalSalesContractDocumentDto?> GetIssuedAsync(Guid contractId, CancellationToken ct = default);

    Task<LocalSalesContractHtmlDto> PreviewAsync(Guid contractId, string? language, CancellationToken ct = default);
    Task<LocalSalesContractHtmlDto> IssueAsync(Guid contractId, string? language, Guid actorId, CancellationToken ct = default);
    Task<LocalSalesContractHtmlDto?> GetIssuedHtmlAsync(Guid contractId, CancellationToken ct = default);
    Task<LocalSalesContractHtmlDto> ReprintAsync(Guid contractId, Guid actorId, CancellationToken ct = default);
}
