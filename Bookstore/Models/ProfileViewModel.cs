using System.ComponentModel.DataAnnotations;
using Bookstore.Domain;

namespace Bookstore.Models;

public sealed record ProfileViewModel(Account Account, IReadOnlyList<ShippingAddress> Addresses,
    IReadOnlyList<AccountMail> Mail);

public sealed class AddressInput
{
    public long Id { get; init; }

    [Display(Name = "Tên người nhận"), Required(ErrorMessage = "Nhập tên người nhận."), StringLength(120)]
    public string Recipient { get; init; } = "";

    [Display(Name = "Số điện thoại"), Required(ErrorMessage = "Nhập số điện thoại."), StringLength(10)]
    public string Phone { get; init; } = "";

    [Display(Name = "Tỉnh/thành"), Required(ErrorMessage = "Nhập tỉnh/thành."), StringLength(100)]
    public string Province { get; init; } = "";

    [Display(Name = "Quận/huyện"), Required(ErrorMessage = "Nhập quận/huyện."), StringLength(100)]
    public string District { get; init; } = "";

    [Display(Name = "Phường/xã"), Required(ErrorMessage = "Nhập phường/xã."), StringLength(100)]
    public string Ward { get; init; } = "";

    [Display(Name = "Địa chỉ chi tiết"), Required(ErrorMessage = "Nhập địa chỉ chi tiết."), StringLength(180)]
    public string Street { get; init; } = "";

    [Display(Name = "Đặt làm địa chỉ mặc định")]
    public bool IsDefault { get; init; }

    public static AddressInput From(ShippingAddress address) => new()
    {
        Id = address.Id, Recipient = address.Recipient, Phone = address.Phone, Province = address.Province,
        District = address.District, Ward = address.Ward, Street = address.Street, IsDefault = address.IsDefault
    };

    public ShippingAddress ToAddress() => new(Id, Recipient.Trim(), Phone, Province.Trim(), District.Trim(),
        Ward.Trim(), Street.Trim(), IsDefault);
}

