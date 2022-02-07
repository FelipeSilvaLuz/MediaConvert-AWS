
function UploadMidia() {
    var formData = new FormData();
    formData.append("file", $('#file')[0].files[0]);

    apiGuardarArquivo(formData).done(function (retorno) {
        if (retorno.sucesso) {
            $('#file').val('');
            window.location.replace('/');
        }
    });

    $('#file').val('');
}

function apiGuardarArquivo(dados) {
    let urlGuardarArquivo = $("#urlGuardarArquivo").val();
    return $.ajax({
        url: urlGuardarArquivo,
        data: dados,
        processData: false,
        contentType: false,
        type: "POST",
        async: false
    });
}


function documentCatalogo() {
    $("body").delegate('#uploadMedia', 'click', UploadMidia);
}

$(document).ready(documentCatalogo);