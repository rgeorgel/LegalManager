export function mascaraCpfCnpj(input) {
  let value = input.value.replace(/\D/g, '');
  if (value.length <= 11) {
    value = value.replace(/(\d{3})(\d)/, '$1.$2');
    value = value.replace(/(\d{3})(\d)/, '$1.$2');
    value = value.replace(/(\d{3})(\d{1,2})$/, '$1-$2');
  } else {
    value = value.replace(/^(\d{2})(\d)/, '$1.$2');
    value = value.replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3');
    value = value.replace(/\.(\d{3})(\d)/, '.$1/$2');
    value = value.replace(/(\d{4})(\d{1,2})$/, '$1-$2');
  }
  input.value = value.substring(0, 18);
}

export function mascaraTelefone(input) {
  let value = input.value.replace(/\D/g, '');
  if (value.length <= 10) {
    value = value.replace(/(\d{2})(\d)/, '($1) $2');
    value = value.replace(/(\d{4})(\d{1,4})$/, '$1-$2');
  } else {
    value = value.replace(/(\d{2})(\d)/, '($1) $2');
    value = value.replace(/(\d{4})(\d{1,5})$/, '$1-$2');
  }
  input.value = value.substring(0, 15);
}

export function mascaraCep(input) {
  let value = input.value.replace(/\D/g, '');
  value = value.replace(/(\d{5})(\d)/, '$1-$2');
  input.value = value.substring(0, 9);
}

export function mascaraOab(input) {
  let value = input.value.toUpperCase().replace(/[^A-Z0-9]/g, '');
  if (value.length > 2) {
    value = value.replace(/^([A-Z]{2})(\d+)$/, '$1 $2');
  }
  input.value = value.substring(0, 10);
}

export function mascaraCnj(input) {
  let value = input.value.replace(/\D/g, '');
  let result = '';
  if (value.length > 0) result += value.substring(0, 7);
  if (value.length > 7) result += '-' + value.substring(7, 9);
  if (value.length > 9) result += '.' + value.substring(9, 13);
  if (value.length > 13) result += '.' + value.substring(13, 14);
  if (value.length > 14) result += '.' + value.substring(14, 16);
  if (value.length > 16) result += '.' + value.substring(16, 20);
  input.value = result;
}

export function aplicarMascaras() {
  document.querySelectorAll('input[type="text"], input[type="tel"]').forEach(input => {
    const id = input.id.toLowerCase();
    const name = input.name?.toLowerCase() || '';
    const placeholder = input.placeholder?.toLowerCase() || '';
    
    if (id.includes('cpf') || id.includes('cnpj') || name.includes('cpf') || name.includes('cnpj') || placeholder.includes('000.000.000')) {
      input.addEventListener('input', () => mascaraCpfCnpj(input));
    }
    else if (id.includes('telefone') || id.includes('fone') || name.includes('telefone') || name.includes('phone') || placeholder.includes('(11)')) {
      input.addEventListener('input', () => mascaraTelefone(input));
    }
    else if (id.includes('cep') || name.includes('cep') || placeholder.includes('00000')) {
      input.addEventListener('input', () => mascaraCep(input));
    }
    else if (id.includes('oab')) {
      input.addEventListener('input', () => mascaraOab(input));
    }
  });
}
