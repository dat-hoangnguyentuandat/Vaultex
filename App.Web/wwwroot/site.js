function submitDeleteForm(id) {
    if (!confirm('Bạn chắc muốn xóa?')) return;
    document.getElementById('delete-product-id').value = id;
    document.getElementById('delete-product-form').submit();
}
