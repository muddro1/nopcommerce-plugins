// Business profile card on the admin customer page: certificates and contacts.
// Everything here posts by AJAX because the card sits inside the core customer form.
(function ($) {
    'use strict';

    function token() {
        return $('input[name="__RequestVerificationToken"]').first().val();
    }

    function showErrors($target, errors) {
        $target.text((errors || []).join(' '));
    }

    function load($container) {
        return $.get($container.data('url')).done(function (html) {
            $container.html(html);
        });
    }

    // nopCommerce's own "Is tax exempt" checkbox is on the same page. If we changed the flag server-side,
    // mirror it here, or clicking Save afterwards would write the stale value back.
    function syncTaxExempt(isTaxExempt) {
        if (typeof isTaxExempt === 'boolean')
            $('#IsTaxExempt').prop('checked', isTaxExempt);
    }

    $(function () {
        var $certificates = $('#cp-certificates');
        var $contacts = $('#cp-contacts');
        if (!$certificates.length)
            return;

        load($certificates);
        load($contacts);

        // ---- Certificates ----

        $('#cp-cert-upload-btn').on('click', function () {
            var $btn = $(this);
            var file = $('#cp-cert-file')[0].files[0];
            var $error = $('#cp-cert-upload-error');
            var data = new FormData();
            data.append('customerId', $certificates.data('customer-id'));
            if (file)
                data.append('file', file);
            data.append('expiresOn', $('#cp-cert-expires').val());
            data.append('certificateNumber', $('#cp-cert-number').val());
            data.append('issuingState', $('#cp-cert-state').val());
            data.append('__RequestVerificationToken', token());

            $btn.prop('disabled', true);
            $.ajax({ url: $btn.data('url'), type: 'POST', data: data, processData: false, contentType: false })
                .done(function (result) {
                    if (!result.success) {
                        showErrors($error, result.errors);
                        return;
                    }
                    showErrors($error, []);
                    $('#cp-cert-file, #cp-cert-expires, #cp-cert-number, #cp-cert-state').val('');
                    syncTaxExempt(result.isTaxExempt);
                    load($certificates);
                })
                .always(function () { $btn.prop('disabled', false); });
        });

        $certificates.on('click', '.cp-cert-approve', function () {
            var $row = $(this).closest('tr');
            var $error = $row.find('.cp-cert-error');
            var postData = { id: $(this).data('id'), expiresOn: $row.find('.cp-cert-approve-date').val(), note: $row.find('.cp-cert-note').val() };
            addAntiForgeryToken(postData);
            $.post($(this).data('url'), postData).done(function (result) {
                if (!result.success) {
                    showErrors($error, result.errors);
                    return;
                }
                syncTaxExempt(result.isTaxExempt);
                load($certificates);
            });
        });

        $certificates.on('click', '.cp-cert-reject', function () {
            var $row = $(this).closest('tr');
            var $error = $row.find('.cp-cert-error');
            var postData = { id: $(this).data('id'), note: $row.find('.cp-cert-note').val() };
            addAntiForgeryToken(postData);
            $.post($(this).data('url'), postData).done(function (result) {
                if (!result.success) {
                    showErrors($error, result.errors);
                    return;
                }
                load($certificates);
            });
        });

        // ---- Contacts ----

        var $form = $('#cp-contact-form');
        var $title = $('#cp-contact-form-title');

        function resetContactForm() {
            $('#cp-contact-id').val('0');
            $('#cp-contact-name, #cp-contact-email, #cp-contact-title').val('');
            $('#cp-contact-active').prop('checked', true);
            $('.cp-contact-category').prop('checked', false);
            $title.text($title.data('add'));
            showErrors($('#cp-contact-error'), []);
        }

        $contacts.on('click', '.cp-contact-edit', function () {
            var c = $(this).data();
            $('#cp-contact-id').val(c.id);
            $('#cp-contact-name').val(c.name);
            $('#cp-contact-email').val(c.email);
            $('#cp-contact-title').val(c.title);
            $('#cp-contact-active').prop('checked', c.active === true || c.active === 'True');
            $('.cp-contact-category').each(function () {
                $(this).prop('checked', (c.categories & parseInt(this.value, 10)) !== 0);
            });
            $title.text($title.data('edit'));
            $('html, body').animate({ scrollTop: $form.offset().top - 80 }, 200);
        });

        $contacts.on('click', '.cp-contact-delete', function () {
            if (!window.confirm($(this).data('confirm')))
                return;
            var postData = { id: $(this).data('id'), customerId: $(this).data('customer-id') };
            addAntiForgeryToken(postData);
            $.post($(this).data('url'), postData).done(function () {
                load($contacts);
                resetContactForm();
            });
        });

        $('#cp-contact-cancel').on('click', resetContactForm);

        $('#cp-contact-save').on('click', function () {
            var categories = $('.cp-contact-category:checked').map(function () { return this.value; }).get();
            var postData = {
                customerId: $contacts.data('customer-id'),
                Id: $('#cp-contact-id').val(),
                Name: $('#cp-contact-name').val(),
                Email: $('#cp-contact-email').val(),
                Title: $('#cp-contact-title').val(),
                Active: $('#cp-contact-active').is(':checked'),
                Categories: categories
            };
            addAntiForgeryToken(postData);
            $.ajax({ url: $form.data('url'), type: 'POST', data: postData, traditional: true }).done(function (result) {
                if (!result.success) {
                    showErrors($('#cp-contact-error'), result.errors);
                    return;
                }
                resetContactForm();
                load($contacts);
            });
        });
    });
})(jQuery);
